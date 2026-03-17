namespace MembershipService.Domain.Events;

public sealed record SubscriptionCanceledEvent(
    Guid SubscriptionId,
    int UserId,
    DateTime CanceledAt,
    DateTime CurrentPeriodEnd) : IDomainEvent
{
    // CurrentPeriodEnd is included because downstream consumers need to know
    // WHEN to revoke entitlements — not immediately, but at period end.
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}
