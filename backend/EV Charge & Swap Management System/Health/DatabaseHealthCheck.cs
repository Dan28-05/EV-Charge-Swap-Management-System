using EVChargeSwap.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EVChargeSwap.Health;

public sealed class DatabaseHealthCheck(
    IServiceScopeFactory scopeFactory,
    ILogger<DatabaseHealthCheck> logger) : IHealthCheck
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
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Log full diagnostic internally; health HTTP response stays sanitized.
            logger.LogWarning(ex, "Database readiness check failed.");
            return HealthCheckResult.Unhealthy("Database is unavailable.");
        }
    }
}
