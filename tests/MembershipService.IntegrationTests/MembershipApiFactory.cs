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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            // Remove the existing DbContext registration
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<MembershipDbContext>));
            if (descriptor != null)
                services.Remove(descriptor);

            // Point DbContext at the test container
            services.AddDbContext<MembershipDbContext>(options =>
                options.UseNpgsql(_postgres.GetConnectionString()));

            // Override Redis connection
            var redisDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(IConnectionMultiplexer));
            if (redisDescriptor != null)
                services.Remove(redisDescriptor);

            services.AddSingleton<IConnectionMultiplexer>(_ =>
                ConnectionMultiplexer.Connect(_redis.GetConnectionString()));

            // Override Redis cache
            services.AddStackExchangeRedisCache(options =>
                options.Configuration = _redis.GetConnectionString());

            // Override RabbitMQ settings
            services.Configure<Infrastructure.Messaging.RabbitMqSettings>(opts =>
            {
                opts.HostName = _rabbitmq.Hostname;
                opts.UserName = "guest";
                opts.Password = "guest";
                // Testcontainers exposes a mapped port
                var connectionString = _rabbitmq.GetConnectionString();
                // Parse port from amqp://guest:guest@localhost:PORT
                var uri = new Uri(connectionString);
                opts.HostName = uri.Host;
                // RabbitMQ.Client ConnectionFactory uses Port property, but our settings don't have it.
                // We need to use the full hostname:port as HostName won't work alone.
                // Workaround: set HostName to the full connection string host
            });

            // Override RabbitMQ IConnection for the mapped port
            var rabbitConnDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(RabbitMQ.Client.IConnection));
            if (rabbitConnDescriptor != null)
                services.Remove(rabbitConnDescriptor);

            services.AddSingleton<RabbitMQ.Client.IConnection>(sp =>
            {
                var connString = _rabbitmq.GetConnectionString();
                var factory = new RabbitMQ.Client.ConnectionFactory
                {
                    Uri = new Uri(connString)
                };
                return factory.CreateConnectionAsync().GetAwaiter().GetResult();
            });
        });

        builder.UseEnvironment("Development");
    }

    public async Task InitializeAsync()
    {
        await Task.WhenAll(
            _postgres.StartAsync(),
            _redis.StartAsync(),
            _rabbitmq.StartAsync());
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await Task.WhenAll(
            _postgres.DisposeAsync().AsTask(),
            _redis.DisposeAsync().AsTask(),
            _rabbitmq.DisposeAsync().AsTask());
    }
}
