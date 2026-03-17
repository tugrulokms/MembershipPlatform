using MembershipService.Domain.Enums;
using MembershipService.Infrastructure.Persistence;
using MembershipService.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MembershipService.Infrastructure.Jobs;

public sealed class SubscriptionExpirationJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SubscriptionExpirationJob> _logger;
    private readonly IOptions<SubscriptionSettings> _settings;

    public SubscriptionExpirationJob(
        IServiceScopeFactory scopeFactory,
        IOptions<SubscriptionSettings> settings,
        ILogger<SubscriptionExpirationJob> logger
        )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _settings = settings;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ProcessExpirations(stoppingToken); }
            catch (Exception ex) { _logger.LogError(0, ex, "Unhandled error during subscription expiration processing!"); }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }

    private async Task ProcessExpirations(CancellationToken ct)
    {
        const int batchSize = 20;
        int subCount = batchSize;

        do
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MembershipDbContext>();
            var subscriptions = await db.Subscriptions
            .Where(s => (s.Status == SubscriptionStatus.PastDue && s.PastDueAt!.Value.AddDays(_settings.Value.GracePeriodDays) < DateTime.UtcNow) || (s.Status == SubscriptionStatus.Canceled && s.CurrentPeriodEnd < DateTime.UtcNow))
            .OrderBy(s => s.CurrentPeriodStart)
            .Take(batchSize)
            .ToListAsync(ct);

            foreach (var subscription in subscriptions)
                subscription.Expire();

            if (subscriptions.Count > 0)
            {
                await db.SaveChangesAsync(ct);
                _logger.LogInformation("Expired {Count} subscriptions.", subscriptions.Count);
            }

            subCount = subscriptions.Count;

        } while (subCount == batchSize);
            
    }
}
