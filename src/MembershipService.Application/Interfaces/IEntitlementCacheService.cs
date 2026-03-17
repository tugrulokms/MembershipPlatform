using MembershipService.Application.DTOs;

namespace MembershipService.Application.Interfaces;

public interface IEntitlementCacheService
{
    Task<IReadOnlyList<EntitlementDto>?> GetEntitlementsByUserAsync(int userId);

    Task SetEntitlementsAsync(int userId, IReadOnlyList<EntitlementDto> entitlements);

    Task InvalidateAsync(int userId);
}