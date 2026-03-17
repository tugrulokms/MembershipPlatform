namespace MembershipService.Infrastructure.Messaging;

// This is the contract the Billing Adapter publishes.
// It's defined here because this is the external event format we consume,
// not a domain event we produce. Different ownership.
public sealed record BillingEvent
{
    public required string EventId { get; init; }
    public required string EventType { get; init; }
    public required Guid SubscriptionId { get; init; }
    public required int UserId { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public required DateTime OccurredAt { get; init; }
}
