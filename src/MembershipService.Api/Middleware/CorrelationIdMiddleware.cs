using MembershipService.Application.Interfaces;
using Serilog.Context;

namespace MembershipService.Api.Middleware;

public class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ICorrelationIdProvider correlationIdProvider)
    {
        string correlationId = context.Request.Headers.TryGetValue("X-Correlation-Id", out var raw) ? raw.ToString() : Guid.NewGuid().ToString();
            
        correlationIdProvider.Set(correlationId);

        context.Response.Headers.Append("X-Correlation-Id", correlationId);

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }
}