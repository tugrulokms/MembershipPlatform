using MembershipService.Domain.Enums;

namespace MembershipService.Domain.Entities
{
    public class Plan
    {
        public int Id { get; set; }
        public required string Name { get; set; }
        public required decimal Price { get; set; }
        public Currency Currency { get; set; }
        public BillingPeriod BillingPeriod { get; set; }
        public required bool IsActive { get; set; }
        public required DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
    }
}