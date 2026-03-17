using System.Text;
using MembershipService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace MembershipService.Infrastructure.Messaging;

public sealed class OutboxProcessor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxProcessor> _logger;
    private readonly RabbitMqSettings _settings;

    // Why IServiceScopeFactory instead of injecting DbContext directly?
    // BackgroundService is a singleton. DbContext is scoped.
    // You can't inject scoped into singleton. So we create a new scope
    // (and a new DbContext) on each polling cycle.
    public OutboxProcessor(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxProcessor> logger,
        IOptions<RabbitMqSettings> settings)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _settings = settings.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _settings.HostName,
            UserName = _settings.UserName,
            Password = _settings.Password,
        };

        // RabbitMQ.Client 7.x uses async connections
        await using var connection = await factory.CreateConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        // Declare a topic exchange. Topic exchanges route messages based on the routing key
        // pattern. Example: "SubscriptionCanceled" routes to queues bound with that key.
        await channel.ExchangeDeclareAsync(
            exchange: _settings.Exchange,
            type: ExchangeType.Topic,
            durable: true,
            cancellationToken: stoppingToken);

        _logger.LogInformation("OutboxProcessor started. Polling for outbox messages...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOutboxMessages(channel, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing outbox messages");
            }

            // Poll interval. In production, you might use a more sophisticated
            // approach (e.g., LISTEN/NOTIFY in Postgres for instant notification).
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    private async Task ProcessOutboxMessages(IChannel channel, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MembershipDbContext>();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        try
        {
            // Fetch unprocessed messages, oldest first. Batch of 20 to avoid holding
            // the connection too long.
            var messages = await db.OutboxMessages
                .FromSqlRaw("""
                    SELECT * FROM "OutboxMessages"
                    WHERE "ProcessedAt" IS NULL
                    ORDER BY "CreatedAt"
                    LIMIT 20
                    FOR UPDATE SKIP LOCKED
                """)
                .ToListAsync(ct);



            foreach (var message in messages)
            {
                var body = Encoding.UTF8.GetBytes(message.Payload);

                var properties = new BasicProperties
                {
                    MessageId = message.Id.ToString(),
                    ContentType = "application/json",
                    CorrelationId = message.CorrelationId,
                    DeliveryMode = DeliveryModes.Persistent, // Survives broker restarts
                };


                // Routing key = event type name (e.g., "SubscriptionCanceledEvent")
                await channel.BasicPublishAsync(
                    exchange: _settings.Exchange,
                    routingKey: message.EventType,
                    mandatory: false,
                    basicProperties: properties,
                    body: body,
                    cancellationToken: ct);

                message.ProcessedAt = DateTime.UtcNow;

                _logger.LogInformation("Published outbox message {MessageId} [{EventType}]",
                message.Id, message.EventType);
            }

            if (messages.Count > 0)
                await db.SaveChangesAsync(ct);

            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }
}
