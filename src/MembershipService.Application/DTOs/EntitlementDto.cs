namespace MembershipService.Application.DTOs;

public class EntitlementDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public Guid SubscriptionId { get; set; }
    public required string FeatureKey { get; set; }
    public required bool IsEnabled { get; set; }
    public int? Limit { get; set; }
    public int? CurrentUsage { get; set; }
}