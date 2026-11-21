using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace EVChargeSwap.Features.Users.Hubs;

public sealed record NotificationPayload(
    Guid Id,
    Guid UserId,
    string Title,
    string Message,
    string Type,
    string? ActionUrl,
    bool IsRead,
    DateTimeOffset CreatedAtUtc);

[Authorize]
public sealed class NotificationHub : Hub
{
    public static string GroupFor(Guid userId) => $"user:{userId:D}";

    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? Context.User?.FindFirst("sub")?.Value;

        if (Guid.TryParse(userId, out var id))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(id));
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? Context.User?.FindFirst("sub")?.Value;

        if (Guid.TryParse(userId, out var id))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupFor(id));
        }

        await base.OnDisconnectedAsync(exception);
    }
}
