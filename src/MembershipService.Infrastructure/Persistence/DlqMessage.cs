using MembershipService.Infrastructure.Enums;

namespace MembershipService.Infrastructure.Persistence;

public class DlqMessage
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public required string EventType { get; set;}
    public required string Payload { get; set; }
    public required string FailureReason { get; set; }
    public int ReplayCount { get; set; }
    public DlqMessageStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastReplayedAt { get; set; }
}   