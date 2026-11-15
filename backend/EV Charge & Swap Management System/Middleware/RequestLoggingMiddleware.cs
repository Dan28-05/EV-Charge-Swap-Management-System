using System.Diagnostics;

namespace EVChargeSwap.Middleware;

public sealed class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var started = Stopwatch.GetTimestamp();
        var requestId = Activity.Current?.Id ?? context.TraceIdentifier;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Request-ID"] = requestId;
            return Task.CompletedTask;
        });

        try
        {
            await next(context);
        }
        finally
        {
            // Deliberately omit query strings, headers and bodies: they may contain secrets.
            logger.LogInformation("HTTP {Method} {Path} returned {StatusCode} in {ElapsedMs:F1} ms. RequestId: {RequestId}",
                context.Request.Method, context.Request.Path, context.Response.StatusCode,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds, requestId);
        }
    }
}
