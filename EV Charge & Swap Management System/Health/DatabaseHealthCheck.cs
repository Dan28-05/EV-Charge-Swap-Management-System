using EVChargeSwap.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EVChargeSwap.Health;

public sealed class DatabaseHealthCheck(IServiceScopeFactory scopeFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        try
        {
            return await db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Database is unavailable.");
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Health responses must not expose connection strings or provider exceptions.
            return HealthCheckResult.Unhealthy("Database is unavailable.");
        }
    }
}
