using EVChargeSwap.Features.Locations.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EVChargeSwap.Features.Locations.Services;

public sealed class StationNotifier(
    IHubContext<StationHub> hubContext,
    IServiceScopeFactory scopeFactory,
    ILogger<StationNotifier> logger) : IStationNotifier
{
    public async Task NotifyStationInventoryChangedAsync(Guid locationId, CancellationToken ct = default)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var locationService = scope.ServiceProvider.GetRequiredService<LocationService>();
            var station = await locationService.GetAsync(locationId, ct);
            if (station is null) return;

            var payload = new StationInventoryUpdatedEvent(
                station.Id,
                station.AvailableChargingBays,
                station.AvailableBatteries,
                DateTimeOffset.UtcNow
            );

            await hubContext.Clients.Group(StationHub.GroupFor(locationId))
                .SendAsync("StationInventoryUpdated", payload, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to broadcast station inventory update for location {LocationId}", locationId);
        }
    }
}
