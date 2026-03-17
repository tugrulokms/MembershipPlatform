using MembershipService.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace MembershipService.Application.Subscriptions.Cancel;

public sealed class CancelSubscriptionHandler
{
    private readonly IEntitlementCacheService _entitlementCacheService;
    private readonly ISubscriptionRepository _subscriptionRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<CancelSubscriptionHandler> _logger;

    public CancelSubscriptionHandler(
        IEntitlementCacheService entitlementCacheService,
        ISubscriptionRepository subscriptionRepository,
        IUnitOfWork unitOfWork,
        ILogger<CancelSubscriptionHandler> logger)
    {
        _entitlementCacheService = entitlementCacheService;
        _subscriptionRepository = subscriptionRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task HandleAsync(CancelSubscriptionCommand command)
    {
        var subscription = await _subscriptionRepository.GetByIdAsync(command.SubscriptionId);
        if (subscription is null)
            throw new InvalidOperationException($"Subscription {command.SubscriptionId} not found.");

        subscription.Cancel();

        await _unitOfWork.SaveChangesAsync();
        
        try
        {
            await _entitlementCacheService.InvalidateAsync(subscription.UserId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Cache invalidation failed for user {UserId}", subscription.UserId);
        }
    }
}