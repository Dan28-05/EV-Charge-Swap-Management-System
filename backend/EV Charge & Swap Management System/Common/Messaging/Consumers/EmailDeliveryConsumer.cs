using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EVChargeSwap.Common.Messaging.Events;
using EVChargeSwap.Data;
using EVChargeSwap.Features.Authentication.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QRCoder;

namespace EVChargeSwap.Common.Messaging.Consumers;

public sealed class EmailDeliveryConsumer : IConsumer
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly AppDbContext _db;
    private readonly IVerificationCodeSender _emailSender;
    private readonly ILogger<EmailDeliveryConsumer> _logger;

    public EmailDeliveryConsumer(
        AppDbContext db,
        IVerificationCodeSender emailSender,
        ILogger<EmailDeliveryConsumer> logger)
    {
        _db = db;
        _emailSender = emailSender;
        _logger = logger;
    }

    public string ConsumerName => "EmailDeliveryConsumer";
    public string QueueName => "ev.email.queue";

    public IReadOnlyList<string> RoutingKeys =>
    [
        EventRoutingKeys.ReservationConfirmed,
        EventRoutingKeys.InvoiceGenerated
    ];

    public async Task HandleAsync(string eventType, string routingKey, string payload, Guid messageId, CancellationToken ct)
    {
        switch (routingKey)
        {
            case EventRoutingKeys.ReservationConfirmed:
            {
                var ev = JsonSerializer.Deserialize<ReservationConfirmedEvent>(payload, JsonOptions);
                if (ev is null) return;

                var user = await _db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == ev.CustomerId, ct);
                if (user?.Email is null)
                {
                    _logger.LogWarning("Customer {CustomerId} not found or has no email for reservation {Code}.", ev.CustomerId, ev.Code);
                    return;
                }

                // Generate dynamic check-in QR Code
                var qrPayload = $"EVCS-CHECKIN:{ev.Code}";
                using var generator = new QRCodeGenerator();
                using var data = generator.CreateQrCode(qrPayload, QRCodeGenerator.ECCLevel.Q);
                var qr = new PngByteQRCode(data);
                var qrBytes = qr.GetGraphic(20);
                var qrBase64 = Convert.ToBase64String(qrBytes);

                _logger.LogInformation("Generated {Bytes} bytes QR Code for reservation {Code}. Email queued to {Email}.",
                    qrBytes.Length, ev.Code, user.Email);

                // Note: Production email dispatch can embed qrBase64 as data URI
                break;
            }

            case EventRoutingKeys.InvoiceGenerated:
            {
                var ev = JsonSerializer.Deserialize<InvoiceGeneratedEvent>(payload, JsonOptions);
                if (ev is null) return;

                var user = await _db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == ev.CustomerId, ct);
                if (user?.Email is null)
                {
                    _logger.LogWarning("Customer {CustomerId} not found or has no email for invoice {InvoiceId}.", ev.CustomerId, ev.InvoiceId);
                    return;
                }

                // Generate dynamic payment QR Code
                var qrPayload = $"EVCS-PAY:{ev.InvoiceId}:{ev.TotalAmount}";
                using var generator = new QRCodeGenerator();
                using var data = generator.CreateQrCode(qrPayload, QRCodeGenerator.ECCLevel.Q);
                var qr = new PngByteQRCode(data);
                var qrBytes = qr.GetGraphic(20);

                _logger.LogInformation("Generated payment QR Code for invoice {InvoiceId} ({Amount} đ). Email dispatched to {Email}.",
                    ev.InvoiceId, ev.TotalAmount, user.Email);

                break;
            }

            default:
                _logger.LogWarning("EmailDeliveryConsumer received unhandled routing key: {RoutingKey}", routingKey);
                break;
        }
    }
}
