using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace MembershipService.Infrastructure.Health;

public sealed class PostgresHealthCheck : IHealthCheck
{
    private readonly string _connectionString;

    public PostgresHealthCheck(IConfiguration config)
        => _connectionString = config.GetConnectionString("MembershipDb")!;
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand("SELECT 1", connection);
            var result = await command.ExecuteScalarAsync(cancellationToken);

            return (result is int i && i == 1)
                ? HealthCheckResult.Healthy("PostgreSQL OK")
                : HealthCheckResult.Unhealthy("PostgreSQL unexpected result");

        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL failed", ex);
        }
    }
}