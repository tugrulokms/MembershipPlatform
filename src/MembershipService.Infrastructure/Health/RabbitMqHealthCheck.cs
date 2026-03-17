using Microsoft.Extensions.Diagnostics.HealthChecks;
using RabbitMQ.Client;

namespace MembershipService.Infrastructure.Health;

public class RabbitMqHealthCheck : IHealthCheck
{
    private readonly IConnection _connection;

    public RabbitMqHealthCheck(IConnection connection) => _connection = connection;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var result = _connection.IsOpen
            ? HealthCheckResult.Healthy("RabbitMQ OK")
            : HealthCheckResult.Unhealthy("RabbitMQ connection closed");
        
        return Task.FromResult(result);
    }
}