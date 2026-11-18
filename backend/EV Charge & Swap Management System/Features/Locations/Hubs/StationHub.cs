using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace EVChargeSwap.Features.Locations.Hubs;

[AllowAnonymous]
public sealed class StationHub : Hub
{
    public static string GroupFor(Guid locationId) => $"station:{locationId:D}";

    public async Task WatchStation(Guid locationId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(locationId));
    }

    public async Task UnwatchStation(Guid locationId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupFor(locationId));
    }
}
