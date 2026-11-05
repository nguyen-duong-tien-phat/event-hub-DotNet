using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using StackExchange.Redis;

namespace EventHub.Health;

/// <summary>Can the API reach PostgreSQL right now? Used by /health/ready.</summary>
public class DatabaseHealthCheck(NpgsqlDataSource dataSource) : IHealthCheck {
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) {
        try {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        } catch (Exception e) {
            return HealthCheckResult.Unhealthy("PostgreSQL is not reachable.", e);
        }
    }
}

/// <summary>Can the API reach Redis right now? Used by /health/ready.</summary>
public class RedisHealthCheck(IConnectionMultiplexer redis) : IHealthCheck {
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) {
        try {
            await redis.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy();
        } catch (Exception e) {
            return HealthCheckResult.Unhealthy("Redis is not reachable.", e);
        }
    }
}
