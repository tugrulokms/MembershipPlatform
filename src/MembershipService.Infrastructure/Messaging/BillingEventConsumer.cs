using System.Text;
using System.Text.Json;
using MembershipService.Application.Interfaces;
using MembershipService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Serilog.Context;

namespace MembershipService.Infrastructure.Messaging;

public sealed class BillingEventConsumer : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BillingEventConsumer> _logger;
    private readonly RabbitMqSettings _settings;

    private const string QueueName = "membership.billing-events";
    private const string DlqName = "membership.billing-events.dlq";
    private const int maxRetryLimit = 3;

    public BillingEventConsumer(
        IServiceScopeFactory scopeFactory,
        ILogger<BillingEventConsumer> logger,
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

        var connection = await factory.CreateConnectionAsync(stoppingToken);
        var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        // Ensure the exchange exists (idempotent — safe to declare multiple times)
        await channel.ExchangeDeclareAsync(
            exchange: _settings.Exchange,
            type: ExchangeType.Topic,
            durable: true,
            cancellationToken: stoppingToken);

        await channel.ExchangeDeclareAsync(
            exchange: _settings.ExchangeDlx,
            type: ExchangeType.Topic,
            durable: true,
            cancellationToken: stoppingToken);

        // Declare our queue — durable so it survives broker restarts
        await channel.QueueDeclareAsync(
            queue: QueueName,
            durable: true,
            exclusive: false,   // Not exclusive — multiple instances can consume
            autoDelete: false,  // Don't delete when last consumer disconnects
            cancellationToken: stoppingToken,
            arguments: new Dictionary<string, object?>
            {
                {"x-dead-letter-exchange", _settings.ExchangeDlx}
            });

        // Declare the DLQ
        await channel.QueueDeclareAsync(
            queue: DlqName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: stoppingToken);

        // Bind: route PaymentSucceeded and PaymentFailed events to our queue
        await channel.QueueBindAsync(QueueName, _settings.Exchange, "PaymentSucceeded", cancellationToken: stoppingToken);
        await channel.QueueBindAsync(QueueName, _settings.Exchange, "PaymentFailed", cancellationToken: stoppingToken);
        await channel.QueueBindAsync(DlqName, _settings.ExchangeDlx, "#", cancellationToken: stoppingToken);

        // Prefetch = 1: process one message at a time per instance.
        // Conservative but safe — ensures we don't have multiple unacked messages
        // that would all get requeued on crash.
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (sender, ea) =>
        {
            try
            {
                var body = Encoding.UTF8.GetString(ea.Body.ToArray());
                var billingEvent = JsonSerializer.Deserialize<BillingEvent>(body);

                if (billingEvent is null)
                {
                    _logger.LogWarning("Received null billing event, discarding");
                    await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, stoppingToken);
                    return;
                }

                _logger.LogInformation(
                    "Received billing event {EventId} [{EventType}] for subscription {SubscriptionId}",
                    billingEvent.EventId, billingEvent.EventType, billingEvent.SubscriptionId);

                var correlationId = ea.BasicProperties.CorrelationId;
                using (LogContext.PushProperty("CorrelationId", correlationId))
                {
                    await ProcessBillingEvent(billingEvent, correlationId);
                }
                
                // ACK only after successful processing.
                // If we crash before this line, RabbitMQ requeues the message.
                await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing billing event");

                var retryCount = 0;

                if(ea.BasicProperties.Headers?.TryGetValue("x-retry-count", out var raw) == true)
                    retryCount = Convert.ToInt32(raw);

                if (retryCount < maxRetryLimit)
                {
                    // Exponential backoff with jitter: 1s, 2s, 4s + random 0-500ms
                    var baseDelay = TimeSpan.FromSeconds(Math.Pow(2, retryCount));
                    var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500));
                    await Task.Delay(baseDelay + jitter, stoppingToken);

                    var props = new BasicProperties
                    {
                        Headers = new Dictionary<string, object?>
                        {
                            {"x-retry-count", retryCount + 1},
                            {"x-failure-reason", ex.Message}
                        },
                        ContentType = "application/json",
                        DeliveryMode = DeliveryModes.Persistent
                    };

                    await channel.BasicPublishAsync(
                        exchange: _settings.Exchange,
                        routingKey: ea.RoutingKey,
                        mandatory: false,
                        basicProperties: props,
                        body: ea.Body,
                        cancellationToken: stoppingToken
                    );

                    await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, stoppingToken);
                }
                else
                {
                    await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, cancellationToken: stoppingToken);
                }

            }
        };

        await channel.BasicConsumeAsync(
            queue: QueueName,
            autoAck: false,   // Manual ack — WE control when the message is confirmed
            consumer: consumer,
            cancellationToken: stoppingToken);

        _logger.LogInformation("BillingEventConsumer started. Listening on queue '{Queue}'", QueueName);

        // Keep the background service alive until shutdown
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task ProcessBillingEvent(BillingEvent billingEvent, string? correlationId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MembershipDbContext>();
        var cache = scope.ServiceProvider.GetRequiredService<IEntitlementCacheService>();

        var correlationIdProvider = scope.ServiceProvider.GetRequiredService<ICorrelationIdProvider>();

        correlationIdProvider.Set(correlationId);

        var subscription = await db.Subscriptions
            .FirstOrDefaultAsync(s => s.Id == billingEvent.SubscriptionId);

        if (subscription is null)
        {
            _logger.LogWarning("Subscription {Id} not found for billing event {EventId}",
                billingEvent.SubscriptionId, billingEvent.EventId);
            return; // Ack anyway — retrying won't help if the subscription doesn't exist
        }

        var billingEventId = Guid.Parse(billingEvent.EventId);

        var message = await db.InboxMessages.FirstOrDefaultAsync(i => i.EventId == billingEventId);

        if (message == null)
        {
            message = new InboxMessage
            {
                EventId = billingEventId,
                ProcessedAt = DateTime.UtcNow
            };

            await db.AddAsync(message);
        }

        else
        {
            _logger.LogWarning("Billing event {EventId} is already exists in inbox messages.", billingEventId);
            return;
        }

        // Stale event check: reject out-of-order events using the timestamp
        if (subscription.IsStaleEvent(billingEvent.OccurredAt))
        {
            _logger.LogInformation(
                "Discarding stale billing event {EventId} (occurred {OccurredAt}, last processed {Last})",
                billingEvent.EventId, billingEvent.OccurredAt, subscription.LastBillingEventTimestamp);
            return;
        }

        for (int retryCount = 0; retryCount < maxRetryLimit; retryCount++)
        {    
            try
            {
                switch (billingEvent.EventType)
                {
                    case "PaymentSucceeded":
                        subscription.Activate();
                        break;
                    case "PaymentFailed":
                        subscription.MarkPastDue();
                        break;
                    default:
                        _logger.LogWarning("Unknown billing event type: {Type}", billingEvent.EventType);
                    return;
                }   
                subscription.LastBillingEventTimestamp = billingEvent.OccurredAt;
                await db.SaveChangesAsync();
                try
                {
                    await cache.InvalidateAsync(subscription.UserId);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Cache failure does not fail the business operation.
                    // Stale data expires via TTL at worst.
                    _logger.LogWarning(ex, "Cache invalidation failed for user {UserId}", subscription.UserId);
                }
                
                _logger.LogInformation(
                    "Processed billing event {EventId}: " +
                    "subscription {Id} → {Status}",
                    billingEvent.EventId,
                    subscription.Id,
                    subscription.Status);
                return;
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning("Billing event {EventId} not applicable to subscription {Id} in state {Status}: {Message}",
        billingEvent.EventId, subscription.Id, subscription.Status, ex.Message);
                return;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogWarning("Update failed because of {Exception}. Retries left: {retryCount}", ex, retryCount);
                db.Entry(subscription).State = EntityState.Detached;
                subscription = await db.Subscriptions
                    .FirstOrDefaultAsync(s => s.Id == billingEvent.SubscriptionId);

                if (subscription is null)
                {
                    _logger.LogWarning("Subscription {Id} was deleted during retry", billingEvent.SubscriptionId);
                    return;
                }
            }
        }
        throw new InvalidOperationException(
    $"Failed to process billing event {billingEvent.EventId} after {maxRetryLimit} retries due to concurrency conflicts.");

    }
}
