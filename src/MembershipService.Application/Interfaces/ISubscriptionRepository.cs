using MembershipService.Domain.Entities;

namespace MembershipService.Application.Interfaces;

public interface ISubscriptionRepository
{
    Task<Subscription?> GetByIdAsync(Guid id);
    Task<Subscription?> GetActiveByUserIdAsync(int userId);
    Task AddAsync(Subscription subscription);
}