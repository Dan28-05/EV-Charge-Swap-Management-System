namespace EVChargeSwap.Features.Locations.Search;

internal sealed class LocationSearchInitializer(IServiceScopeFactory scopes, ElasticLocationIndex index,
    ILogger<LocationSearchInitializer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!index.Enabled) return;

        // Also recover automatically if the backing index or volume is lost after startup.
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<LocationIndexer>().EnsureInitializedAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Could not initialize the station search index. Will retry.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
