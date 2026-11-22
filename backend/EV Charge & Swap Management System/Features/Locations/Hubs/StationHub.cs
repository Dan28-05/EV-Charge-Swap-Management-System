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

    public async Task<IReadOnlyList<StationInventoryUpdatedEvent>> WatchStations(Guid[]? locationIds)
    {
        var requested = locationIds ?? [];
        if (requested.Length > 100) throw new HubException("Too many station subscriptions.");

        var next = requested.Where(id => id != Guid.Empty).ToHashSet();
        var current = Context.Items.TryGetValue(SubscriptionKey, out var value) && value is HashSet<Guid> ids
            ? ids
            : [];
        Context.Items[SubscriptionKey] = current;

        var ct = Context.ConnectionAborted;
        foreach (var id in current.Except(next).ToArray())
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupFor(id), ct);
            current.Remove(id);
        }

        foreach (var id in next.Except(current).ToArray())
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(id), ct);
            current.Add(id);
        }

        return current.Count == 0
            ? []
            : await locations.GetInventorySnapshotAsync(current.ToArray(), ct);
    }

    public async Task UnwatchStation(Guid locationId)
    {
        if (locationId == Guid.Empty) return;
        if (Context.Items.TryGetValue(SubscriptionKey, out var value) && value is HashSet<Guid> ids && ids.Contains(locationId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupFor(locationId), Context.ConnectionAborted);
            ids.Remove(locationId);
        }
    }
}
