using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EVChargeSwap.Common.Messaging.Events;
using EVChargeSwap.Data;
using EVChargeSwap.Features.Reservations.Entities;
using EVChargeSwap.Features.Reservations.Services;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EVChargeSwap.Common.Messaging.Consumers;

public sealed class BillingConsumer : IConsumer
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly AppDbContext _db;
    private readonly IOutboxService _outboxService;
    private readonly ILogger<BillingConsumer> _logger;

    public BillingConsumer(
        AppDbContext db,
        IOutboxService outboxService,
        ILogger<BillingConsumer> logger)
    {
        _db = db;
        _outboxService = outboxService;
        _logger = logger;
    }

    public string ConsumerName => "BillingConsumer";
    public string QueueName => "ev.billing.queue";

    public IReadOnlyList<string> RoutingKeys =>
    [
        EventRoutingKeys.ChargingSessionCompleted, EventRoutingKeys.BatterySwapCompleted
    ];

    public async Task HandleAsync(string eventType, string routingKey, string payload, Guid messageId, CancellationToken ct)
    {
        Invoice invoice;
        if (routingKey == EventRoutingKeys.ChargingSessionCompleted)
        {
            var ev = JsonSerializer.Deserialize<ChargingSessionCompletedEvent>(payload, JsonOptions)
                ?? throw new JsonException("Missing charging completion event.");
            var rate = ev.AppliedKwhRate ?? TariffLookup.LegacyChargingRate;
            var amount = Math.Round(ev.TotalKwh * rate, 2);
            invoice = new Invoice { ReservationId = ev.ReservationId, CustomerId = ev.CustomerId,
                TotalKwh = ev.TotalKwh, KwhRate = rate, EnergyAmount = amount, TotalAmount = amount };
        }
        else if (routingKey == EventRoutingKeys.BatterySwapCompleted)
        {
            var ev = JsonSerializer.Deserialize<BatterySwapCompletedEvent>(payload, JsonOptions)
                ?? throw new JsonException("Missing battery swap event.");
            invoice = new Invoice { ReservationId = ev.ReservationId, CustomerId = ev.CustomerId,
                KwhRate = 0m, SwapRate = ev.AppliedRate, TotalAmount = ev.AppliedRate };
        }
        else return;

        // Check if invoice already created for this reservation
        var existingInvoice = await _db.Invoices.AnyAsync(x => x.ReservationId == invoice.ReservationId, ct);
        if (existingInvoice)
        {
            return;
        }

        _db.Invoices.Add(invoice);

        // Enqueue outbound event for notifications & emails
        await _outboxService.EnqueueAsync(
            "InvoiceGenerated",
            EventRoutingKeys.InvoiceGenerated,
            new InvoiceGeneratedEvent(
                invoice.Id,
                invoice.ReservationId,
                invoice.CustomerId,
                invoice.TotalKwh,
                invoice.EnergyAmount,
                invoice.TotalAmount,
                invoice.CreatedAtUtc,
                invoice.SwapRate.HasValue),
            ct);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_invoices_ReservationId" })
        {
            _db.ChangeTracker.Clear();
            return;
        }

        _logger.LogInformation(
            "Generated invoice {InvoiceId} for reservation {ReservationId}: {Kwh} kWh, total {Total} VND.",
            invoice.Id, invoice.ReservationId, invoice.TotalKwh, invoice.TotalAmount);
    }
}
