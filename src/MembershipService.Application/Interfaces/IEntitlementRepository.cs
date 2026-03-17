using MembershipService.Domain.Entities;

namespace MembershipService.Application.Interfaces;

public interface IEntitlementRepository
{
    Task<IReadOnlyList<Entitlement>> GetByUserIdAsync(int userId);
}
