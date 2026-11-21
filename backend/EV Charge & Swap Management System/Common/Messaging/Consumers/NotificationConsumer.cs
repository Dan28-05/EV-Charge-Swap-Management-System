using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EVChargeSwap.Common.Messaging.Events;
using EVChargeSwap.Data;
using EVChargeSwap.Features.Reservations.Entities;
using EVChargeSwap.Features.Users.Entities;
using EVChargeSwap.Features.Users.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace EVChargeSwap.Common.Messaging.Consumers;

public sealed class NotificationConsumer : IConsumer
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly AppDbContext _db;
    private readonly ILogger<NotificationConsumer> _logger;
    private readonly TimeProvider _clock;
    private readonly IHubContext<NotificationHub>? _hubContext;

    public NotificationConsumer(
        AppDbContext db,
        ILogger<NotificationConsumer> logger,
        TimeProvider clock,
        IHubContext<NotificationHub>? hubContext = null)
    {
        _db = db;
        _logger = logger;
        _clock = clock;
        _hubContext = hubContext;
    }

    public string ConsumerName => "NotificationConsumer";
    public string QueueName => "ev.notifications.queue";

    public IReadOnlyList<string> RoutingKeys =>
    [
        EventRoutingKeys.ReservationConfirmed,
        EventRoutingKeys.ChargingSessionCompleted,
        EventRoutingKeys.InvoiceGenerated,
        EventRoutingKeys.BayFaulted,
        EventRoutingKeys.IncidentReported,
        EventRoutingKeys.IncidentReservationResolved,
        EventRoutingKeys.PaymentSucceeded
    ];

    public async Task HandleAsync(string eventType, string routingKey, string payload, Guid messageId, CancellationToken ct)
    {
        switch (routingKey)
        {
            case EventRoutingKeys.ReservationConfirmed:
            {
                var ev = JsonSerializer.Deserialize<ReservationConfirmedEvent>(payload, JsonOptions);
                if (ev is null) return;

                var notification = new Notification
                {
                    UserId = ev.CustomerId,
                    Title = "Xác nhận đặt chỗ thành công",
                    Message = $"Mã đặt chỗ {ev.Code} đã được xác nhận. Hết hạn giữ chỗ lúc {ev.ExpiresAtUtc.ToLocalTime():HH:mm dd/MM/yyyy}.",
                    Type = NotificationType.Info,
                    ActionUrl = "/reservations"
                };
                await SaveAndPublishNotificationAsync(notification, ct);
                _logger.LogInformation("Saved reservation confirmation notification for customer {CustomerId}.", ev.CustomerId);
                break;
            }

            case EventRoutingKeys.ChargingSessionCompleted:
            {
                var ev = JsonSerializer.Deserialize<ChargingSessionCompletedEvent>(payload, JsonOptions);
                if (ev is null) return;

                var notification = new Notification
                {
                    UserId = ev.CustomerId,
                    Title = "Phiên sạc pin hoàn thành",
                    Message = $"Phiên sạc kết thúc lúc {ev.CompletedAtUtc.ToLocalTime():HH:mm}. Tổng tiêu thụ: {ev.TotalKwh:F3} kWh. Vui lòng báo nhân viên khi xe rời chỗ sạc.",
                    Type = NotificationType.Success,
                    ActionUrl = "/reservations"
                };
                await SaveAndPublishNotificationAsync(notification, ct);
                _logger.LogInformation("Saved charging completed notification for customer {CustomerId}.", ev.CustomerId);
                break;
            }

            case EventRoutingKeys.InvoiceGenerated:
            {
                var ev = JsonSerializer.Deserialize<InvoiceGeneratedEvent>(payload, JsonOptions);
                if (ev is null) return;

                var notification = new Notification
                {
                    UserId = ev.CustomerId,
                    Title = "Hóa đơn dịch vụ mới",
                    Message = $"Hóa đơn {(ev.IsBatterySwap ? "đổi pin" : "tiền điện")} {ev.TotalAmount:N2} đ đã phát hành.",
                    Type = NotificationType.Warning,
                    ActionUrl = "/invoices"
                };
                await SaveAndPublishNotificationAsync(notification, ct);
                _logger.LogInformation("Saved invoice generated notification for customer {CustomerId}.", ev.CustomerId);
                break;
            }

            case EventRoutingKeys.PaymentSucceeded:
            {
                var ev = JsonSerializer.Deserialize<PaymentSucceededEvent>(payload, JsonOptions);
                if (ev is null) return;
                await SaveIncidentNotificationAsync(new Notification
                {
                    Id = messageId, UserId = ev.CustomerId, Title = "Thanh toán thành công",
                    Message = "Hóa đơn của bạn đã được thanh toán.", ActionUrl = "/invoices",
                    Type = NotificationType.Success
                }, ct);
                break;
            }

            case EventRoutingKeys.BayFaulted:
            {
                var ev = JsonSerializer.Deserialize<ChargingBayFaultedEvent>(payload, JsonOptions);
                if (ev is not null) await NotifyBayFaultAsync(ev, ct);
                break;
            }

            case EventRoutingKeys.IncidentReported:
            {
                var ev = JsonSerializer.Deserialize<IncidentReportedEvent>(payload, JsonOptions);
                if (ev is not null) await NotifyIncidentReportedAsync(ev.IncidentId, messageId, ct);
                break;
            }

            case EventRoutingKeys.IncidentReservationResolved:
            {
                var ev = JsonSerializer.Deserialize<IncidentReservationResolvedEvent>(payload, JsonOptions);
                if (ev is null) return;
                var reassigned = ev.ReplacementCode is not null;
                await SaveIncidentNotificationAsync(new Notification
                {
                    Id = messageId,
                    UserId = ev.CustomerId,
                    Title = reassigned ? "Đã đổi tài nguyên giữ chỗ" : "Lượt đặt đã hủy do sự cố",
                    Message = reassigned
                        ? $"Nhân viên tại trạm đã chuyển lượt đặt của bạn sang {ev.ReplacementCode}."
                        : "Thiết bị gặp sự cố và trạm không còn tài nguyên thay thế. Nhân viên đã hủy lượt đặt của bạn.",
                    Type = NotificationType.Warning,
                    ActionUrl = "/reservations"
                }, ct);
                break;
            }

            default:
                _logger.LogWarning("NotificationConsumer received unhandled routing key: {RoutingKey}", routingKey);
                break;
        }
    }

    private async Task NotifyIncidentReportedAsync(Guid incidentId, Guid messageId, CancellationToken ct)
    {
        var affected = await (from report in _db.IncidentReports.AsNoTracking()
            join reservation in _db.Reservations.AsNoTracking()
                on report.ReservationId equals (Guid?)reservation.Id
            where report.Id == incidentId && report.Resolution == null &&
                reservation.ChargingBayId == report.ChargingBayId && reservation.BatteryId == report.BatteryId &&
                (report.ChargingBayId != null && _db.ChargingBays.Any(x => x.Id == report.ChargingBayId &&
                    x.EquipmentState == ChargerState.Faulted) ||
                 report.BatteryId != null && _db.Batteries.Any(x => x.Id == report.BatteryId &&
                    x.State == BatteryState.Faulted && x.LocationId == report.LocationId)) &&
                (reservation.Status == ReservationStatus.Held || reservation.Status == ReservationStatus.CheckedIn)
            select new { reservation.CustomerId, reservation.Status, reservation.ExpiresAtUtc, report.ChargingBayId })
            .SingleOrDefaultAsync(ct);
        if (affected is null ||
            (affected.Status == ReservationStatus.Held && affected.ExpiresAtUtc <= _clock.GetUtcNow()))
            return;

        await SaveIncidentNotificationAsync(new Notification
        {
            Id = messageId,
            UserId = affected.CustomerId,
            Title = affected.ChargingBayId.HasValue ? "Trụ sạc đang gặp sự cố" : "Pin giữ chỗ đang gặp sự cố",
            Message = "Thiết bị trong lượt đặt có sự cố. Vui lòng liên hệ nhân viên tại trạm để được hỗ trợ.",
            Type = NotificationType.Warning,
            ActionUrl = "/reservations"
        }, ct);
    }

    private async Task SaveIncidentNotificationAsync(Notification notification, CancellationToken ct)
    {
        if (await _db.Notifications.AnyAsync(x => x.Id == notification.Id, ct)) return;
        try
        {
            await SaveAndPublishNotificationAsync(notification, ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "PK_notifications"
        })
        {
            _db.Entry(notification).State = EntityState.Detached;
        }
    }

    private async Task NotifyBayFaultAsync(ChargingBayFaultedEvent fault, CancellationToken ct)
    {
        // A delayed message may arrive after the bay is repaired or the reservation has changed.
        var reservation = await _db.Reservations.AsNoTracking()
            .Where(x => x.LocationId == fault.LocationId && x.ChargingBayId == fault.ChargingBayId &&
                (x.Status == ReservationStatus.Held || x.Status == ReservationStatus.CheckedIn) &&
                _db.ChargingBays.Any(b => b.Id == fault.ChargingBayId &&
                    b.LocationId == fault.LocationId && b.EquipmentState == ChargerState.Faulted))
            .Select(x => new { x.CustomerId, x.Status, x.CreatedAtUtc, x.ExpiresAtUtc })
            .SingleOrDefaultAsync(ct);
        if (reservation is null || reservation.CreatedAtUtc > fault.FaultedAtUtc ||
            (reservation.Status == ReservationStatus.Held && reservation.ExpiresAtUtc <= _clock.GetUtcNow()))
            return;

        await SaveAndPublishNotificationAsync(new Notification
        {
            UserId = reservation.CustomerId,
            Title = "Trụ sạc đang gặp sự cố",
            Message = $"Trụ {fault.BayCode} có sự cố. Vui lòng liên hệ nhân viên tại trạm để được hỗ trợ.",
            Type = NotificationType.Warning,
            ActionUrl = "/reservations"
        }, ct);
    }

    private async Task SaveAndPublishNotificationAsync(Notification notification, CancellationToken ct)
    {
        _db.Notifications.Add(notification);
        await _db.SaveChangesAsync(ct);

        if (_hubContext is not null)
        {
            var payloadDto = new NotificationPayload(
                notification.Id,
                notification.UserId,
                notification.Title,
                notification.Message,
                notification.Type.ToString(),
                notification.ActionUrl,
                notification.IsRead,
                notification.CreatedAtUtc);

            try
            {
                await _hubContext.Clients.Group($"user:{notification.UserId}")
                    .SendAsync("ReceiveNotification", payloadDto, ct);
                await _hubContext.Clients.User(notification.UserId.ToString())
                    .SendAsync("ReceiveNotification", payloadDto, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to broadcast notification over SignalR for user {UserId}.", notification.UserId);
            }
        }
    }
}
