using EVChargeSwap.Features.Locations.Hubs;
using EVChargeSwap.Data;
using EVChargeSwap.Features.Reservations.Entities;
using EVChargeSwap.Features.Users.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EVChargeSwap.Features.Locations.Services;

public sealed class StationNotifier(
    IHubContext<StationHub> hubContext,
    IHubContext<NotificationHub> privateHub,
    IServiceScopeFactory scopeFactory,
    ILogger<StationNotifier> logger) : IStationNotifier
{
    public async Task NotifyStationInventoryChangedAsync(Guid locationId, CancellationToken ct = default)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var locationService = scope.ServiceProvider.GetRequiredService<LocationService>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var assignedOperators = await db.Set<OperatorAssignment>().AsNoTracking()
                .Where(x => x.LocationId == locationId && x.EndedAtUtc == null)
                .Select(x => x.OperatorId).ToListAsync(ct);
            if (assignedOperators.Count != 0)
                await privateHub.Clients.Groups(assignedOperators.Select(NotificationHub.GroupFor).ToArray())
                    .SendAsync("OperatorStationChanged", ct);

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
