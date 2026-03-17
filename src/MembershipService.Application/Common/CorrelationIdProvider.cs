using MembershipService.Application.Interfaces;

namespace MembershipService.Application.Common;

public class CorrelationIdProvider : ICorrelationIdProvider
{
    private string? _correlationId;
    public string? Get()
    {
        return _correlationId;
    }

    public void Set(string? correlationId)
    {
        _correlationId = correlationId;
    }
}