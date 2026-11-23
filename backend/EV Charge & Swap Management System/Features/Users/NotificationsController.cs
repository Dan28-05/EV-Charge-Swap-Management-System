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
    int UnreadCount,
    int TotalCount,
    int PageSize);

[ApiController]
[Authorize]
[Route("api/notifications")]
public sealed class NotificationsController(AppDbContext db, TimeProvider? clock = null) : ControllerBase
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    [HttpGet]
    public async Task<ActionResult<NotificationsListResponse>> GetNotifications(CancellationToken ct,
        [FromQuery] int page = 1, [FromQuery] string category = "all")
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        const int pageSize = 20;
        if (page < 1 || page > int.MaxValue / pageSize || category is not ("all" or "booking" or "charging" or "payment"))
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: "Invalid pagination or category filter.");

        var query = db.Notifications.AsNoTracking().Where(x => x.UserId == userId);
        if (category != "all")
        {
            var categorized = query.Select(x => new { Item = x, Text = (x.Title + " " + x.Message).ToLower() })
                .Select(x => new
                {
                    x.Item,
                    Category = x.Text.Contains("thanh toán") || x.Text.Contains("hóa đơn") ||
                        x.Text.Contains("tiền") || x.Text.Contains("qr") ? "payment"
                        : x.Text.Contains("lượt đặt") || x.Text.Contains("đặt chỗ") ? "booking"
                        : x.Text.Contains("sạc") || x.Text.Contains("kwh") || x.Text.Contains("pin") ? "charging"
                        : "booking"
                });
            query = categorized.Where(x => x.Category == category).Select(x => x.Item);
        }
        var total = await query.CountAsync(ct);
        var offset = (page - 1) * pageSize;
        if (db.Database.IsNpgsql())
            query = query.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id).Skip(offset).Take(pageSize);

        var rawItems = await query
            .Select(x => new NotificationResponse(
                x.Id,
                x.Title,
                x.Message,
                x.Type.ToString(),
                x.ActionUrl,
                x.IsRead,
                x.CreatedAtUtc))
            .ToListAsync(ct);

        var items = db.Database.IsNpgsql() ? rawItems : rawItems
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id)
            .Skip(offset).Take(pageSize)
            .ToList();

        var unreadCount = await db.Notifications.CountAsync(x => x.UserId == userId && !x.IsRead, ct);

        return Ok(new NotificationsListResponse(items, unreadCount, total, pageSize));
    }

    [HttpPut("{id:guid}/read")]
    public async Task<IActionResult> MarkAsRead(Guid id, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var notification = await db.Notifications
            .SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);

        if (notification is null) return NotFound();

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAtUtc = _clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
        }

        return NoContent();
    }

    [HttpPut("read-all")]
    public async Task<IActionResult> MarkAllAsRead(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var now = _clock.GetUtcNow();
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
