using MembershipService.Application.Interfaces;
using MembershipService.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MembershipService.Infrastructure.Persistence.Repositories;

public sealed class EntitlementRepository : IEntitlementRepository
{
    private readonly MembershipDbContext _db;

    public EntitlementRepository(MembershipDbContext db) => _db = db;

    public async Task<IReadOnlyList<Entitlement>> GetByUserIdAsync(int userId)
        => await _db.Entitlements.AsNoTracking().Where(e => e.UserId == userId).ToListAsync();
}
