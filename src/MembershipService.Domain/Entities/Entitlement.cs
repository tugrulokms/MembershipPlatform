using MembershipService.Domain.Enums;

namespace MembershipService.Domain.Entities
{
    public class Entitlement
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public Guid SubscriptionId { get; set; }
        public required string FeatureKey { get; set; }
        public required EntitlementType Type { get; set; }
        public required bool IsEnabled { get; set; }
        public int? Limit { get; set; }
        public int? CurrentUsage { get; set; }
    }
}