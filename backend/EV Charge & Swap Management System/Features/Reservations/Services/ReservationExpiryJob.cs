namespace EVChargeSwap.Features.Reservations.Services;

internal sealed class ReservationExpiryJob(
    IServiceScopeFactory scopeFactory, TimeProvider clock, ILogger<ReservationExpiryJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ReservationService>().ExpireAsync(stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(exception, "Failed to expire reservations. The job will retry on the next tick.");
            }
        }
    }
}
