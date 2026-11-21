namespace EVChargeSwap.Tests.Integration;

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EVChargeSwap.Common.Messaging;
using EVChargeSwap.Common.Messaging.Consumers;
using EVChargeSwap.Common.Messaging.Events;
using EVChargeSwap.Data;
using EVChargeSwap.Features.Locations.Entities;
using EVChargeSwap.Features.Reservations.Entities;
using EVChargeSwap.Features.Users.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public sealed class MessagingIntegrationTests
{
    private static ApplicationUser CreateUser(Guid id, string email)
    {
        return new ApplicationUser(email, "Integration Tester", DateTime.UtcNow)
        {
            Id = id,
            NormalizedEmail = email.ToUpperInvariant(),
            NormalizedUserName = email.ToUpperInvariant()
        };
    }

    [Fact]
    public async Task EndToEnd_HoldReservation_PublishesOutbox_AndDispatchesNotification()
    {
        using var factory = new ApiFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var outboxService = scope.ServiceProvider.GetRequiredService<IOutboxService>();

        var customerId = Guid.NewGuid();
        var user = CreateUser(customerId, "customer_e2e@example.com");
        db.Users.Add(user);

        var locationId = factory.ActiveLocationId;
        var resId = Guid.NewGuid();
        const string resCode = "RES-E2E-TEST";

        // 1. Enqueue ReservationConfirmed outbox event (simulates HoldAsync outbox participation)
        await outboxService.EnqueueAsync(
            "ReservationConfirmed",
            EventRoutingKeys.ReservationConfirmed,
            new ReservationConfirmedEvent(
                resId,
                customerId,
                locationId,
                resCode,
                "Charging",
                DateTimeOffset.UtcNow.AddMinutes(15),
                DateTimeOffset.UtcNow),
            CancellationToken.None);

        await db.SaveChangesAsync();

        // 2. Verify outbox table atomically captured the event
        var outboxMessage = await db.OutboxMessages
            .SingleOrDefaultAsync(x => x.EventType == "ReservationConfirmed" && x.RoutingKey == EventRoutingKeys.ReservationConfirmed);
        Assert.NotNull(outboxMessage);
        Assert.Null(outboxMessage.ProcessedAtUtc);

        // 3. Worker consumer receives and handles event
        var notificationConsumer = new NotificationConsumer(db, NullLogger<NotificationConsumer>.Instance, TimeProvider.System);
        await notificationConsumer.HandleAsync(
            outboxMessage.EventType,
            outboxMessage.RoutingKey,
            outboxMessage.Payload,
            outboxMessage.Id,
            CancellationToken.None);

        // 4. Verify in-app notification created for customer
        var notification = await db.Notifications.SingleOrDefaultAsync(x => x.UserId == customerId);
        Assert.NotNull(notification);
        Assert.Contains(resCode, notification.Message);
        Assert.False(notification.IsRead);
    }

    [Fact]
    public async Task EndToEnd_CompleteChargingSession_CalculatesBill_AndQueuesDownstreamEvents()
    {
        using var factory = new ApiFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var outboxService = scope.ServiceProvider.GetRequiredService<IOutboxService>();

        var locationId = factory.ActiveLocationId;
        var customerId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var bayId = Guid.NewGuid();
        var resId = Guid.NewGuid();

        db.Users.AddRange(
            CreateUser(customerId, "customer_billing@example.com"),
            CreateUser(operatorId, "operator_billing@example.com"));

        var bay = new ChargingBay
        {
            Id = bayId,
            LocationId = locationId,
            Code = "BAY-BILL-E2E",
            PowerKw = 60,
            EquipmentState = ChargerState.Active,
            ParkingState = ParkingState.Occupied
        };
        db.ChargingBays.Add(bay);

        var reservation = new Reservation();
        db.Entry(reservation).Property(nameof(Reservation.Id)).CurrentValue = resId;
        db.Entry(reservation).Property(nameof(Reservation.LocationId)).CurrentValue = locationId;
        db.Entry(reservation).Property(nameof(Reservation.CustomerId)).CurrentValue = customerId;
        db.Entry(reservation).Property(nameof(Reservation.ChargingBayId)).CurrentValue = bayId;
        db.Entry(reservation).Property(nameof(Reservation.Code)).CurrentValue = "RES-BILL-E2E";
        db.Entry(reservation).Property(nameof(Reservation.Status)).CurrentValue = ReservationStatus.CheckedIn;
        db.Entry(reservation).Property(x => x.CheckedInAtUtc).CurrentValue = DateTimeOffset.UtcNow.AddMinutes(-60);
        db.Entry(reservation).Property(x => x.CheckedInById).CurrentValue = operatorId;
        db.Entry(reservation).Property(nameof(Reservation.VehicleArrivedAtUtc)).CurrentValue = DateTimeOffset.UtcNow.AddMinutes(-60);
        db.Entry(reservation).Property(x => x.VehicleArrivedById).CurrentValue = operatorId;
        db.Entry(reservation).Property(nameof(Reservation.ExpiresAtUtc)).CurrentValue = DateTimeOffset.UtcNow.AddHours(2);
        db.Entry(reservation).Property(nameof(Reservation.CreatedAtUtc)).CurrentValue = DateTimeOffset.UtcNow.AddMinutes(-60);
        db.Reservations.Add(reservation);

        // 1. Enqueue ChargingSessionCompleted event (Delta = 20.0 kWh)
        var completedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        var startedAt = completedAt.AddMinutes(-50);
        var chargingEvent = new ChargingSessionCompletedEvent(
            SessionId: Guid.NewGuid(),
            ReservationId: resId,
            CustomerId: customerId,
            ChargingBayId: bayId,
            StartMeterKwh: 100.0m,
            EndMeterKwh: 120.0m,
            TotalKwh: 20.0m,
            StartedAtUtc: startedAt,
            CompletedAtUtc: completedAt);

        await outboxService.EnqueueAsync(
            "ChargingSessionCompleted",
            EventRoutingKeys.ChargingSessionCompleted,
            chargingEvent,
            CancellationToken.None);

        await db.SaveChangesAsync();

        // 2. Billing consumer processes the event
        var billingConsumer = new BillingConsumer(db, outboxService, NullLogger<BillingConsumer>.Instance);
        var payload = JsonSerializer.Serialize(chargingEvent);
        await billingConsumer.HandleAsync(
            "ChargingSessionCompleted",
            EventRoutingKeys.ChargingSessionCompleted,
            payload,
            Guid.NewGuid(),
            CancellationToken.None);

        // 3. Verify Invoice record created
        var invoice = await db.Invoices.SingleOrDefaultAsync(x => x.ReservationId == resId);
        Assert.NotNull(invoice);
        Assert.Equal(20.0m, invoice.TotalKwh);
        Assert.Equal(3858m, invoice.KwhRate);
        Assert.Equal(20.0m * 3858m, invoice.EnergyAmount);
        Assert.Equal(invoice.EnergyAmount, invoice.TotalAmount);

        // 4. Verify downstream InvoiceGenerated outbox event was queued
        var invoiceOutbox = await db.OutboxMessages
            .SingleOrDefaultAsync(x => x.EventType == "InvoiceGenerated");
        Assert.NotNull(invoiceOutbox);

        // 5. Notification consumer processes the downstream InvoiceGenerated event
        var notificationConsumer = new NotificationConsumer(db, NullLogger<NotificationConsumer>.Instance, TimeProvider.System);
        await notificationConsumer.HandleAsync(
            invoiceOutbox.EventType,
            invoiceOutbox.RoutingKey,
            invoiceOutbox.Payload,
            invoiceOutbox.Id,
            CancellationToken.None);

        // 6. Verify notification created for customer with the invoice total amount
        var invoiceNotification = await db.Notifications
            .SingleOrDefaultAsync(x => x.UserId == customerId && x.Title.Contains("Hóa đơn"));
        Assert.NotNull(invoiceNotification);
        Assert.Contains(invoice.TotalAmount.ToString("N2"), invoiceNotification.Message);
    }

    [Fact]
    public async Task FaultTolerance_BrokerDown_PreservesOutboxInDatabase()
    {
        using var factory = new ApiFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var outboxService = scope.ServiceProvider.GetRequiredService<IOutboxService>();

        // When RabbitMQ is unreachable or down, API enqueueing succeeds cleanly
        var testEvent = new { Ping = "Test", Value = 42 };
        await outboxService.EnqueueAsync("PingEvent", "test.ping", testEvent, CancellationToken.None);
        await db.SaveChangesAsync();

        var message = await db.OutboxMessages.SingleOrDefaultAsync(x => x.RoutingKey == "test.ping");
        Assert.NotNull(message);
        Assert.Null(message.ProcessedAtUtc);
        Assert.Equal(0, message.RetryCount);
    }

    [Fact]
    public async Task Idempotency_DuplicateMessages_ProcessedOnlyOnce()
    {
        using var factory = new ApiFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var inboxService = scope.ServiceProvider.GetRequiredService<IInboxService>();

        var messageId = Guid.NewGuid();
        const string consumer = "DuplicateTestConsumer";

        // First delivery: not yet processed
        var alreadyProcessedFirst = await inboxService.HasBeenProcessedAsync(messageId, consumer);
        Assert.False(alreadyProcessedFirst);

        // Mark processed
        await inboxService.MarkAsProcessedAsync(messageId, consumer);

        // Second delivery (duplicate): recognized as already processed
        var alreadyProcessedSecond = await inboxService.HasBeenProcessedAsync(messageId, consumer);
        Assert.True(alreadyProcessedSecond);
    }
}
