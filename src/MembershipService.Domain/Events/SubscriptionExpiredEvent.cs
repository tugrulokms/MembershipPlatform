namespace MembershipService.Domain.Events;

public sealed record SubscriptionExpiredEvent(
    Guid SubscriptionId,
    int UserId,
    DateTime ExpiredAt) : IDomainEvent
{
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}