using MembershipService.Application.Interfaces;
using MembershipService.Domain.Entities;
using MembershipService.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MembershipService.Infrastructure.Persistence.Repositories;

public sealed class SubscriptionRepository : ISubscriptionRepository
{
    private readonly MembershipDbContext _db;

    public SubscriptionRepository(MembershipDbContext db) => _db = db;

    public async Task<Subscription?> GetByIdAsync(Guid id)
        => await _db.Subscriptions.FirstOrDefaultAsync(s => s.Id == id);

    // This query hits the composite index (UserId, Status) we created.
    // Without that index, this would be a full table scan on every subscription check.
    public async Task<Subscription?> GetActiveByUserIdAsync(int userId)
        => await _db.Subscriptions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Status == SubscriptionStatus.Active);

    public async Task AddAsync(Subscription subscription)
        => await _db.Subscriptions.AddAsync(subscription);
}
