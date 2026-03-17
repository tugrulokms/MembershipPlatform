namespace MembershipService.Infrastructure.Persistence;

public class InboxMessage
{
    public Guid EventId { get; set; }
    public DateTime? ProcessedAt { get; set; }
}