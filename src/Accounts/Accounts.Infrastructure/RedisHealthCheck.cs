using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Accounts.Infrastructure;

/// <summary>Ping Redis thật — k8s probe cần biết dependency chết, không chỉ process còn sống.</summary>
public sealed class RedisHealthCheck(string connectionString) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var mux = await ConnectionMultiplexer.ConnectAsync(connectionString);
            await mux.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Redis không phản hồi", ex);
        }
    }
}
