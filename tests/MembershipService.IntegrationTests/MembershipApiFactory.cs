using MembershipService.Infrastructure.Jobs;
using MembershipService.Infrastructure.Messaging;
using MembershipService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;
using Xunit;

namespace MembershipService.IntegrationTests;

public class MembershipApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16")
        .WithDatabase("membership_db")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder()
        .WithImage("redis:7")
        .Build();

    private readonly RabbitMqContainer _rabbitmq = new RabbitMqBuilder()
        .WithImage("rabbitmq:3-management")
        .WithUsername("guest")
        .WithPassword("guest")
        .Build();

    private RabbitMQ.Client.IConnection? _rabbitConnection;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            // Remove only the specific background services that create their own
            // RabbitMQ connections via RabbitMqSettings (which lacks a Port property,
            // so they can't reach Testcontainers' random mapped port).
            var typesToRemove = new[]
            {
                typeof(OutboxProcessor),
                typeof(BillingEventConsumer),
                typeof(DlqConsumer),
                typeof(SubscriptionExpirationJob)
            };
            var descriptorsToRemove = services
                .Where(d => d.ImplementationType != null &&
                            typesToRemove.Contains(d.ImplementationType))
                .ToList();
            foreach (var d in descriptorsToRemove)
                services.Remove(d);

            // DbContext → test PostgreSQL container
            var dbDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<MembershipDbContext>));
            if (dbDescriptor != null)
                services.Remove(dbDescriptor);

            services.AddDbContext<MembershipDbContext>(options =>
                options.UseNpgsql(_postgres.GetConnectionString()));

            // Redis → test container
            var redisDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(IConnectionMultiplexer));
            if (redisDescriptor != null)
                services.Remove(redisDescriptor);

            services.AddSingleton<IConnectionMultiplexer>(_ =>
                ConnectionMultiplexer.Connect(_redis.GetConnectionString()));

            services.AddStackExchangeRedisCache(options =>
                options.Configuration = _redis.GetConnectionString());

            // RabbitMQ IConnection → pre-created connection from InitializeAsync.
            // This avoids the .GetAwaiter().GetResult() deadlock risk inside a
            // DI factory and guarantees the connection is open for health checks.
            var rabbitConnDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(RabbitMQ.Client.IConnection));
            if (rabbitConnDescriptor != null)
                services.Remove(rabbitConnDescriptor);

            services.AddSingleton(_rabbitConnection!);
        });

        // Override connection strings in IConfiguration so that health checks
        // (which read directly from config, not from DI) hit the test containers.
        builder.UseSetting("ConnectionStrings:MembershipDb", _postgres.GetConnectionString());
        builder.UseSetting("ConnectionStrings:Redis", _redis.GetConnectionString());

        builder.UseEnvironment("Development");
    }

    public async Task InitializeAsync()
    {
        await Task.WhenAll(
            _postgres.StartAsync(),
            _redis.StartAsync(),
            _rabbitmq.StartAsync());

        // Create RabbitMQ connection after the container is fully ready
        var factory = new RabbitMQ.Client.ConnectionFactory
        {
            Uri = new Uri(_rabbitmq.GetConnectionString())
        };
        _rabbitConnection = await factory.CreateConnectionAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        if (_rabbitConnection is not null)
            await _rabbitConnection.DisposeAsync();

        await Task.WhenAll(
            _postgres.DisposeAsync().AsTask(),
            _redis.DisposeAsync().AsTask(),
            _rabbitmq.DisposeAsync().AsTask());
    }
}
