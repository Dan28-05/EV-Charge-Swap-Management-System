using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EVChargeSwap.Health;

public static class HealthResponseWriter
{
    public static Task WriteAsync(HttpContext context, HealthReport report) =>
        context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(x => new { name = x.Key, status = x.Value.Status.ToString() })
        }, cancellationToken: context.RequestAborted);
}
