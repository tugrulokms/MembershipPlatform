namespace MembershipService.Application.Interfaces;

public interface ICorrelationIdProvider
{
    string? Get();
    void Set(string? correlationId);
}