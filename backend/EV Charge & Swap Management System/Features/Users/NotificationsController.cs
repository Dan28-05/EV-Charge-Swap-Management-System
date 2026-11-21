using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using EVChargeSwap.Data;
using EVChargeSwap.Features.Users.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EVChargeSwap.Features.Users;

public sealed record NotificationResponse(
    Guid Id,
    string Title,
    string Message,
    string Type,
    string? ActionUrl,
    bool IsRead,
    DateTimeOffset CreatedAtUtc);

public sealed record NotificationsListResponse(
    IReadOnlyList<NotificationResponse> Items,
    int UnreadCount);

[ApiController]
[Authorize]
[Route("api/notifications")]
public sealed class NotificationsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<NotificationsListResponse>> GetNotifications(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var rawItems = await db.Notifications.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Take(100)
            .Select(x => new NotificationResponse(
                x.Id,
                x.Title,
                x.Message,
                x.Type.ToString(),
                x.ActionUrl,
                x.IsRead,
                x.CreatedAtUtc))
            .ToListAsync(ct);

        var items = rawItems
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(50)
            .ToList();

        var unreadCount = await db.Notifications.CountAsync(x => x.UserId == userId && !x.IsRead, ct);

        return Ok(new NotificationsListResponse(items, unreadCount));
    }

    [HttpPut("{id:guid}/read")]
    public async Task<IActionResult> MarkAsRead(Guid id, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var notification = await db.Notifications
            .SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);

        if (notification is null) return NotFound();

        notification.IsRead = true;
        notification.ReadAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    [HttpPut("read-all")]
    public async Task<IActionResult> MarkAllAsRead(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var now = DateTimeOffset.UtcNow;
        await db.Notifications
            .Where(x => x.UserId == userId && !x.IsRead)
            .ExecuteUpdateAsync(update => update
                .SetProperty(x => x.IsRead, true)
                .SetProperty(x => x.ReadAtUtc, now), ct);

        return NoContent();
    }

    private bool TryGetUserId(out Guid id) =>
        Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out id);
}
