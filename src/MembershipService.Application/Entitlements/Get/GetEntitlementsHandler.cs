using MembershipService.Application.DTOs;
using MembershipService.Application.Interfaces;

namespace MembershipService.Application.Entitlements.Get;

public sealed class GetEntitlementsHandler
{
    private readonly IEntitlementRepository _entitlementRepository;
    private readonly IEntitlementCacheService _cacheService;

    public GetEntitlementsHandler(IEntitlementRepository entitlementRepository, IEntitlementCacheService cacheService)
    {
        _entitlementRepository = entitlementRepository;
        _cacheService = cacheService;
    }

    public async Task<IReadOnlyList<EntitlementDto>> HandleAsync(int userId)
    {
        var cached = await _cacheService.GetEntitlementsByUserAsync(userId);
        if (cached is not null)
            return cached;

        var entitlements = await _entitlementRepository.GetByUserIdAsync(userId);

        var dtos = entitlements.Select(e => new EntitlementDto
        {
            Id = e.Id,
            UserId = e.UserId,
            SubscriptionId = e.SubscriptionId,
            FeatureKey = e.FeatureKey,
            IsEnabled = e.IsEnabled,
            Limit = e.Limit,
            CurrentUsage = e.CurrentUsage
        }).ToList();

        // Don't cache empty results — entitlements may be populated asynchronously
        // (e.g. via a domain event handler on SubscriptionCreated). Caching empty
        // would serve stale data until TTL expires even after entitlements are created.
        if (dtos.Count > 0)
            await _cacheService.SetEntitlementsAsync(userId, dtos);

        return dtos;
    }
}
