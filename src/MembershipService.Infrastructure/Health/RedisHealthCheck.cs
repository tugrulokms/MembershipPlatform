using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace MembershipService.Infrastructure.Health;

public class RedisHealthCheck : IHealthCheck
{
    private readonly IConnectionMultiplexer _mux;

    public RedisHealthCheck(IConnectionMultiplexer mux) => _mux = mux;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _mux.GetDatabase();
            var pong = await db.PingAsync();

            return HealthCheckResult.Healthy($"Redis OK (ping {pong.TotalMilliseconds:n0} ms)");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Redis failed", ex);
        }
    }
}