using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using EVChargeSwap.Features.Locations.Services;

namespace EVChargeSwap.Features.Locations.Hubs;

[AllowAnonymous]
public sealed class StationHub(LocationService locations) : Hub
{
    private const string SubscriptionKey = "watched-stations";
    public static string GroupFor(Guid locationId) => $"station:{locationId:D}";

    public Task WatchStation(Guid locationId) => WatchStations([locationId]);

    public async Task<IReadOnlyList<StationInventoryUpdatedEvent>> WatchStations(Guid[] locationIds)
    {
        if (locationIds.Length > 100) throw new HubException("Too many station subscriptions.");
        var next = locationIds.Where(id => id != Guid.Empty).ToHashSet();
        var current = Context.Items.TryGetValue(SubscriptionKey, out var value) && value is HashSet<Guid> ids
            ? ids : [];
        foreach (var id in current.Except(next))
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupFor(id));
        foreach (var id in next.Except(current))
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(id));
        Context.Items[SubscriptionKey] = next;
        return await locations.GetInventorySnapshotAsync(next.ToArray(), Context.ConnectionAborted);
    }

    public async Task UnwatchStation(Guid locationId)
    {
        if (Context.Items.TryGetValue(SubscriptionKey, out var value) && value is HashSet<Guid> ids)
            await WatchStations(ids.Where(id => id != locationId).ToArray());
    }
}
