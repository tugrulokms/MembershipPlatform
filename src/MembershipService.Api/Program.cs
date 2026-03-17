using MembershipService.Api.Middleware;
using MembershipService.Application.Common;
using MembershipService.Application.Entitlements.Get;
using MembershipService.Application.Interfaces;
using MembershipService.Application.Subscriptions.Cancel;
using MembershipService.Application.Subscriptions.Create;
using MembershipService.Infrastructure.Caching;
using MembershipService.Infrastructure.Jobs;
using MembershipService.Infrastructure.Messaging;
using MembershipService.Infrastructure.Persistence;
using MembershipService.Infrastructure.Persistence.Repositories;
using MembershipService.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Formatting.Json;
using MembershipService.Infrastructure.Health;
using StackExchange.Redis;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using RabbitMQ.Client;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

// EF Core
builder.Services.AddDbContext<MembershipDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("MembershipDb")));

// RabbitMQ
builder.Services.Configure<RabbitMqSettings>(builder.Configuration.GetSection("RabbitMq"));
builder.Services.AddHostedService<OutboxProcessor>();
builder.Services.AddHostedService<BillingEventConsumer>();
builder.Services.AddHostedService<DlqConsumer>();
builder.Services.AddSingleton<IConnection>(sp =>
{
    var settings = sp.GetRequiredService<IOptions<RabbitMqSettings>>().Value;
    var factory = new ConnectionFactory
    {
        HostName = settings.HostName,
        UserName = settings.UserName,
        Password = settings.Password
    };
    return factory.CreateConnectionAsync().GetAwaiter().GetResult();
});

// Background Job(s)
builder.Services.Configure<SubscriptionSettings>(
    builder.Configuration.GetSection("Subscription"));
builder.Services.AddHostedService<SubscriptionExpirationJob>();

// Redis
builder.Services.AddStackExchangeRedisCache(options =>
    options.Configuration = builder.Configuration.GetConnectionString("Redis"));
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis")!));


// Dependency Inversion wiring
builder.Services.AddScoped<IPlanRepository, PlanRepository>();
builder.Services.AddScoped<ISubscriptionRepository, SubscriptionRepository>();
builder.Services.AddScoped<IEntitlementRepository, EntitlementRepository>();
builder.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<MembershipDbContext>());
builder.Services.AddScoped<IEntitlementCacheService, EntitlementCacheService>();
builder.Services.AddScoped<ICorrelationIdProvider, CorrelationIdProvider>();

// Application handlers
builder.Services.AddScoped<CreateSubscriptionHandler>();
builder.Services.AddScoped<CancelSubscriptionHandler>();
builder.Services.AddScoped<GetEntitlementsHandler>();


// Logging (Serilog)
builder.Host.UseSerilog((ctx, cfg) =>
cfg.ReadFrom.Configuration(ctx.Configuration).Enrich.FromLogContext().WriteTo.Console(new JsonFormatter())
);

// OpenTelemetry
var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .SetResourceBuilder(ResourceBuilder.CreateDefault()
                .AddService("MembershipService"))
            .AddAspNetCoreInstrumentation()
            .AddEntityFrameworkCoreInstrumentation(options =>
            {
                options.Filter = (_, _) => Activity.Current?.ParentId != null;
            });

        if (!string.IsNullOrEmpty(otlpEndpoint))
            tracing.AddOtlpExporter(opts => opts.Endpoint = new Uri(otlpEndpoint));
        else
            tracing.AddConsoleExporter();
    });

// HealthChecks
builder.Services.AddHealthChecks()
    .AddCheck<PostgresHealthCheck>("postgres")
    .AddCheck<RedisHealthCheck>("redis")
    .AddCheck<RabbitMqHealthCheck>("rabbitmq");

ThreadPool.SetMinThreads(workerThreads: 200, completionPortThreads: 200);

var app = builder.Build();

// Apply pending migrations on startup (safe for single-instance dev/demo)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<MembershipDbContext>();
    db.Database.Migrate();
}

app.MapOpenApi();

app.UseHttpsRedirection();
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseMiddleware<CorrelationIdMiddleware>();

// POST /subscriptions — Create a new subscription
app.MapPost("/subscriptions", async (
    CreateSubscriptionCommand command,
    CreateSubscriptionHandler handler) =>
{
    if (command.UserId <= 0)
        return Results.BadRequest(new { status = 400, error = "UserId must be a positive integer." });
    if (command.PlanId <= 0)
        return Results.BadRequest(new { status = 400, error = "PlanId must be a positive integer." });

    var result = await handler.HandleAsync(command);
    return Results.Created($"/subscriptions/{result.SubscriptionId}", result);
});

// GET /users/{userId}/entitlements — Cache-aside read: Redis → DB → cache
app.MapGet("/users/{userId}/entitlements", async (
    int userId,
    GetEntitlementsHandler handler) =>
{
    if (userId <= 0)
        return Results.BadRequest(new { status = 400, error = "UserId must be a positive integer." });

    var result = await handler.HandleAsync(userId);
    return Results.Ok(result);
});

// POST /subscriptions/{id}/cancel — Cancel a subscription
app.MapPost("/subscriptions/{id}/cancel", async (
    Guid id,
    CancelSubscriptionHandler handler) =>
{
    await handler.HandleAsync(new CancelSubscriptionCommand(id));
    return Results.NoContent();
});

// Liveness — process alive, no dependency checks
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });

// Readiness — all dependency checks must pass
app.MapHealthChecks("/ready");


await app.RunAsync();

// Make the implicit Program class accessible for integration tests
public partial class Program { }
