namespace MembershipService.Domain.Events;

public sealed record SubscriptionCreatedEvent(
    Guid SubscriptionId,
    int UserId,
    int PlanId,
    DateTime CurrentPeriodStart,
    DateTime CurrentPeriodEnd) : IDomainEvent
{
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}
