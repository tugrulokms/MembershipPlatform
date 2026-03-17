namespace MembershipService.Infrastructure.Enums;

public enum DlqMessageStatus
{
    Transient,
    Replayed,
    PermanentlyFailed
}