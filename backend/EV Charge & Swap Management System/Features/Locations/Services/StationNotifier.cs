using EVChargeSwap.Data;
using EVChargeSwap.Features.Locations.Hubs;
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
    ILogger<StationNotifier> logger,
    TimeProvider? clock = null) : IStationNotifier
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task NotifyStationInventoryChangedAsync(Guid locationId, CancellationToken ct = default)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var locationService = scope.ServiceProvider.GetRequiredService<LocationService>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var assignedOperators = await db.Set<OperatorAssignment>().AsNoTracking()
                .Where(x => x.LocationId == locationId && x.EndedAtUtc == null)
                .Select(x => x.OperatorId).ToListAsync(ct);
            if (assignedOperators.Count != 0)
                await privateHub.Clients.Groups(assignedOperators.Select(NotificationHub.GroupFor).ToArray())
                    .SendAsync("OperatorStationChanged", ct);

            // Query authoritative active station availability directly from PostgreSQL (bypassing Redis cache).
            var snapshots = await locationService.GetInventorySnapshotAsync([locationId], ct);
            if (snapshots.Count == 0) return;

            var snapshot = snapshots[0];
            var payload = new StationInventoryUpdatedEvent(
                snapshot.LocationId,
                snapshot.AvailableChargingBays,
                snapshot.AvailableBatteries,
                _clock.GetUtcNow());

            await hubContext.Clients.Group(StationHub.GroupFor(locationId))
                .SendAsync("StationInventoryUpdated", payload, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to broadcast station inventory update for location {LocationId}", locationId);
        }
    }
}
