namespace MembershipService.Application.Subscriptions.Create;

// A command is a simple data object — "here's what the caller wants to do."
// No logic, no dependencies. Just the data needed to execute the use case.
public sealed record CreateSubscriptionCommand(int UserId, int PlanId);
