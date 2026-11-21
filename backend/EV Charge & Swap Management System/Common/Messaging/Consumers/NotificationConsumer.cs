using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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

                var customer = await _db.Users.AsNoTracking()
                    .Where(u => u.Id == ev.CustomerId)
                    .Select(u => new { u.FullName, u.PhoneNumber, u.Email })
                    .FirstOrDefaultAsync(ct);

                var customerLabel = customer is not null && !string.IsNullOrWhiteSpace(customer.FullName)
                    ? customer.FullName
                    : "Quý khách";
                var contactInfo = customer is not null
                    ? (!string.IsNullOrWhiteSpace(customer.PhoneNumber) ? customer.PhoneNumber : customer.Email)
                    : null;
                var custSummary = !string.IsNullOrWhiteSpace(contactInfo)
                    ? $"{customerLabel} ({contactInfo})"
                    : customerLabel;

                var notification = new Notification
                {
                    Id = messageId,
                    UserId = ev.CustomerId,
                    Title = "Xác nhận đặt chỗ thành công",
                    Message = $"Mã đặt chỗ {ev.Code} đã được xác nhận. Hết hạn giữ chỗ lúc {ev.ExpiresAtUtc.ToLocalTime():HH:mm dd/MM/yyyy}.",
                    Type = NotificationType.Info,
                    ActionUrl = "/reservations"
                };
                await SaveOnceAsync(notification, ct);
                var service = ev.ServiceType == nameof(ReservationServiceType.Charging) ? "sạc ô tô" : "đổi pin";
                await NotifyStationOperatorsAsync(ev.LocationId, ev.CreatedAtUtc, messageId,
                    "Lượt đặt mới", $"Khách hàng {custSummary} đã đặt giữ chỗ {service}. Mã: {ev.Code}. Hạn giữ chỗ: {ev.ExpiresAtUtc.ToLocalTime():HH:mm}.", NotificationType.Info,
                    $"/operator/dashboard?reservation={ev.ReservationId:D}", ct);
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

                var invoiceInfo = await (from invoice in _db.Invoices.AsNoTracking()
                    join customer in _db.Users.AsNoTracking() on invoice.CustomerId equals customer.Id into customers
                    from customer in customers.DefaultIfEmpty()
                    join payment in _db.Payments.AsNoTracking().Where(x => x.Status == PaymentStatus.Succeeded) on invoice.Id equals payment.InvoiceId into payments
                    from payment in payments.DefaultIfEmpty()
                    join collector in _db.Users.AsNoTracking() on payment.CollectedByOperatorId equals collector.Id into collectors
                    from collector in collectors.DefaultIfEmpty()
                    join reservation in _db.Reservations.AsNoTracking() on invoice.ReservationId equals reservation.Id into reservations
                    from reservation in reservations.DefaultIfEmpty()
                    where invoice.Id == ev.InvoiceId
                    select new
                    {
                        CustomerName = customer != null ? customer.FullName : "Quý khách",
                        CustomerPhone = customer != null ? customer.PhoneNumber : null,
                        CustomerEmail = customer != null ? customer.Email : null,
                        CollectorName = collector != null ? collector.FullName : null,
                        LocationId = reservation != null ? (Guid?)reservation.LocationId : null,
                        TotalAmount = invoice.TotalAmount,
                        IsCash = payment != null && payment.CollectedByOperatorId.HasValue,
                        Method = payment != null && payment.CollectedByOperatorId.HasValue ? "tiền mặt tại quầy" : "chuyển khoản VietQR",
                        ReservationCode = reservation != null ? reservation.Code : null
                    }).FirstOrDefaultAsync(ct);

                var collectorSuffix = !string.IsNullOrEmpty(invoiceInfo?.CollectorName)
                    ? $" Thu ngân: {invoiceInfo.CollectorName}."
                    : "";

                await SaveOnceAsync(new Notification
                {
                    Id = messageId, UserId = ev.CustomerId, Title = "Thanh toán thành công",
                    Message = $"Hóa đơn {(invoiceInfo?.TotalAmount ?? 0):N0} đ đã được thanh toán qua {invoiceInfo?.Method ?? "hệ thống"}.{collectorSuffix} Cảm ơn Quý khách {invoiceInfo?.CustomerName}!",
                    ActionUrl = "/invoices",
                    Type = NotificationType.Success
                }, ct);

                if (invoiceInfo?.LocationId is { } locId)
                {
                    var custSummary = invoiceInfo.CustomerName + (!string.IsNullOrWhiteSpace(invoiceInfo.CustomerPhone) ? $" ({invoiceInfo.CustomerPhone})" : "");
                    var title = invoiceInfo.IsCash ? "Thanh toán tiền mặt thành công" : "Khách đã thanh toán QR";
                    await NotifyStationOperatorsAsync(locId, _clock.GetUtcNow(), messageId,
                        title,
                        $"Khách hàng {custSummary} đã thanh toán {(invoiceInfo.TotalAmount):N0} đ qua {invoiceInfo.Method}.{collectorSuffix}",
                        NotificationType.Success, $"/operator/dashboard?invoice={ev.InvoiceId:D}", ct);
                }
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
                await SaveOnceAsync(new Notification
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
            select new { reservation.Id, reservation.Status, reservation.ExpiresAtUtc,
                report.LocationId, report.ReportedAtUtc })
            .SingleOrDefaultAsync(ct);
        if (affected is null ||
            (affected.Status == ReservationStatus.Held && affected.ExpiresAtUtc <= _clock.GetUtcNow()))
            return;

        await NotifyStationOperatorsAsync(affected.LocationId, affected.ReportedAtUtc, messageId,
            "Lượt đặt bị ảnh hưởng", "Trụ sạc hoặc pin của lượt đặt gặp sự cố.",
            NotificationType.Warning, $"/operator/dashboard?reservation={affected.Id:D}", ct);
    }

    private async Task NotifyStationOperatorsAsync(Guid locationId, DateTimeOffset occurredAt, Guid messageId,
        string title, string message, NotificationType type, string actionUrl, CancellationToken ct)
    {
        var recipients = await _db.Set<OperatorAssignment>().AsNoTracking()
            .Where(x => x.LocationId == locationId && x.EndedAtUtc == null)
            .Select(x => new { x.OperatorId, x.AssignedAtUtc }).ToListAsync(ct);

        foreach (var recipient in recipients.Where(x => x.AssignedAtUtc == null || x.AssignedAtUtc <= occurredAt))
        {
            // Keep one durable notification per event and recipient on RabbitMQ redelivery.
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"operator-notification:{messageId:N}:{recipient.OperatorId:N}"));
            await SaveOnceAsync(new Notification
            {
                Id = new Guid(hash.AsSpan(0, 16)), UserId = recipient.OperatorId,
                Title = title, Message = message, Type = type, ActionUrl = actionUrl, CreatedAtUtc = occurredAt
            }, ct);
        }
    }

    private async Task SaveOnceAsync(Notification notification, CancellationToken ct)
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
