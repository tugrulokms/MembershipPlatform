using System.Text;
using System.Text.Json;
using MembershipService.Application.Interfaces;
using MembershipService.Infrastructure.Enums;
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

public class DlqConsumer : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DlqConsumer> _logger;
    private readonly RabbitMqSettings _settings;

    private const string QueueName = "membership.billing-events.dlq";

    public DlqConsumer(IServiceScopeFactory scopeFactory, ILogger<DlqConsumer> logger, IOptions<RabbitMqSettings> settings)
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

        await channel.ExchangeDeclareAsync(
            exchange: _settings.ExchangeDlx,
            type: ExchangeType.Topic,
            durable: true,
            cancellationToken: stoppingToken);

        await channel.ExchangeDeclareAsync(
            exchange: _settings.Exchange,
            type: ExchangeType.Topic,
            durable: true,
            cancellationToken: stoppingToken);

        await channel.QueueDeclareAsync(
            queue: QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: stoppingToken);

        await channel.QueueBindAsync(QueueName, _settings.ExchangeDlx, "#", cancellationToken: stoppingToken);

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
                    _logger.LogWarning("Received null dlq message, discarding");
                    await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, stoppingToken);
                    return;
                }

                _logger.LogInformation(
                    "Received billing event {EventId}, Event type: {EventType}",
                    billingEvent.EventId, billingEvent.EventType);

                var correlationId = ea.BasicProperties.CorrelationId;
                var failureReason = "";
                if (ea.BasicProperties.Headers?.TryGetValue("x-failure-reason", out var rawReason) == true)
                    failureReason = rawReason is byte[] bytes ? Encoding.UTF8.GetString(bytes) : rawReason?.ToString() ?? "";

                using (LogContext.PushProperty("CorrelationId", correlationId))
                {

                    var dlq = await ProcessDlqMessages(billingEvent, correlationId, body, failureReason);

                    if (dlq.Status == DlqMessageStatus.Replayed)
                    {
                        var props = new BasicProperties
                        {
                            Headers = new Dictionary<string, object?>
                            {
                                {"x-retry-count", 0}
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
                        await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, stoppingToken);

                        _logger.LogWarning("DLQ message with ID [{Id}], eventID [{EventId}], Event Type: [{EventType}] and  replay count {ReplayCount} wasn't able to be published. Changing status to [{Status}]", dlq.Id, dlq.EventId, dlq.EventType, dlq.ReplayCount, dlq.Status);
                    }
                             
                }
                  
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing DLQ messages.");
            }   
            
        };

        await channel.BasicConsumeAsync(
            queue: QueueName,
            autoAck: false,   // Manual ack — WE control when the message is confirmed
            consumer: consumer,
            cancellationToken: stoppingToken);

        _logger.LogInformation("DlqConsumer started. Listening on queue '{Queue}'", QueueName);

        await Task.Delay(Timeout.Infinite, stoppingToken);

    }

    private async Task<DlqMessage> ProcessDlqMessages(BillingEvent billingEvent, string? correlationId, string body, string failureReason)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MembershipDbContext>();
        var correlationIdProvider = scope.ServiceProvider.GetRequiredService<ICorrelationIdProvider>();

        correlationIdProvider.Set(correlationId);

        var eventId = Guid.Parse(billingEvent.EventId);

        var dlq = await db.DlqMessages.FirstOrDefaultAsync(m => m.EventId == eventId);
        if (dlq is null)
        {
            dlq = new DlqMessage
            {
                Id = Guid.NewGuid(),
                EventId = eventId,
                EventType = billingEvent.EventType,
                Payload = body,
                ReplayCount = 0,
                FailureReason = failureReason,
                CreatedAt = DateTime.UtcNow
            };
            db.DlqMessages.Add(dlq);
        }
        else
        {
            dlq.EventId = eventId;
            dlq.EventType = billingEvent.EventType;
            dlq.Payload = body;
            dlq.FailureReason = failureReason;
        }

        bool replay = dlq.ReplayCount < 3;

        if (replay)
        {
            dlq.Status = DlqMessageStatus.Replayed;
            dlq.ReplayCount++;
            dlq.LastReplayedAt = DateTime.UtcNow;
        }
        else
        {
            dlq.Status = DlqMessageStatus.PermanentlyFailed;
        }

        await db.SaveChangesAsync();

            
        _logger.LogInformation("DLQ message [{Id}], with event ID [{EventId}] in state {Status} is saved.", dlq.Id, dlq.EventId, dlq.Status);

        return dlq;
    }
}