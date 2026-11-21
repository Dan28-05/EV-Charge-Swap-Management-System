namespace EVChargeSwap.Tests.Unit;

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EVChargeSwap.Common.Messaging;
using EVChargeSwap.Common.Messaging.Consumers;
using EVChargeSwap.Common.Messaging.Events;
using EVChargeSwap.Data;
using EVChargeSwap.Features.Authentication.Services;
using EVChargeSwap.Features.Locations.Entities;
using EVChargeSwap.Features.Reservations.Entities;
using EVChargeSwap.Features.Users.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public sealed class ConsumerTests
{
    private static async Task<AppDbContext> CreateInMemoryDbContextAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection, contextOwnsConnection: true).Options;
        var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private static ApplicationUser CreateTestUser(Guid id, string email)
    {
        return new ApplicationUser(email, "Test User", DateTime.UtcNow)
        {
            Id = id,
            NormalizedEmail = email.ToUpperInvariant(),
            NormalizedUserName = email.ToUpperInvariant()
        };
    }

    [Fact]
    public async Task InboxService_PreventsDuplicateProcessing()
    {
        await using var db = await CreateInMemoryDbContextAsync();
        var inboxService = new InboxService(db, NullLogger<InboxService>.Instance);
        var messageId = Guid.NewGuid();
        const string consumerName = "TestConsumer";

        // Initially not processed
        Assert.False(await inboxService.HasBeenProcessedAsync(messageId, consumerName));

        // Mark as processed
        await inboxService.MarkAsProcessedAsync(messageId, consumerName);

        // Now must return true
        Assert.True(await inboxService.HasBeenProcessedAsync(messageId, consumerName));

        // Different consumer must still return false
        Assert.False(await inboxService.HasBeenProcessedAsync(messageId, "AnotherConsumer"));
    }

    [Theory]
    [InlineData(false, null, 3858, 40512.86)]
    [InlineData(true, 4500, 4500, 47254.50)]
    public async Task BillingConsumer_ChargesOnlyEnergy_UsingSnapshottedRate(
        bool vehicleDeparted, int? appliedRate, int expectedRate, double expectedAmount)
    {
        await using var db = await CreateInMemoryDbContextAsync();
        var outboxService = new OutboxService(db, NullLogger<OutboxService>.Instance);
        var billingConsumer = new BillingConsumer(db, outboxService, NullLogger<BillingConsumer>.Instance);

        var location = new Location("Station A", "Address A", 10.5m, 106.5m, true, true);
        var locationId = location.Id;
        var customerId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var bayId = Guid.NewGuid();

        // Seed Location and User
        var customer = CreateTestUser(customerId, "billing_test@example.com");
        var operatorId = Guid.NewGuid();
        db.Locations.Add(location);
        db.Users.AddRange(customer, CreateTestUser(operatorId, "operator_billing_test@example.com"));

        var bay = new ChargingBay
        {
            Id = bayId,
            LocationId = locationId,
            Code = "BAY-BILLING",
            PowerKw = 60
        };
        db.ChargingBays.Add(bay);

        var completedAt = DateTimeOffset.UtcNow.AddMinutes(-15);
        var startedAt = completedAt.AddMinutes(-45);
        var reservation = new Reservation();
        db.Entry(reservation).Property(nameof(Reservation.Id)).CurrentValue = reservationId;
        db.Entry(reservation).Property(nameof(Reservation.LocationId)).CurrentValue = locationId;
        db.Entry(reservation).Property(nameof(Reservation.CustomerId)).CurrentValue = customerId;
        db.Entry(reservation).Property(nameof(Reservation.ChargingBayId)).CurrentValue = bayId;
        db.Entry(reservation).Property(nameof(Reservation.Code)).CurrentValue = "RES-BILLING";
        db.Entry(reservation).Property(nameof(Reservation.CreatedAtUtc)).CurrentValue = DateTimeOffset.UtcNow.AddHours(-1);
        db.Entry(reservation).Property(nameof(Reservation.ExpiresAtUtc)).CurrentValue = DateTimeOffset.UtcNow.AddHours(1);
        db.Entry(reservation).Property(x => x.Status).CurrentValue = vehicleDeparted
            ? ReservationStatus.Completed : ReservationStatus.CheckedIn;
        db.Entry(reservation).Property(x => x.CheckedInAtUtc).CurrentValue = startedAt;
        db.Entry(reservation).Property(x => x.CheckedInById).CurrentValue = operatorId;
        db.Entry(reservation).Property(x => x.VehicleArrivedAtUtc).CurrentValue = startedAt;
        db.Entry(reservation).Property(x => x.VehicleArrivedById).CurrentValue = operatorId;
        if (vehicleDeparted)
        {
            var departedAt = completedAt.AddMinutes(10);
            db.Entry(reservation).Property(x => x.VehicleDepartedAtUtc).CurrentValue = departedAt;
            db.Entry(reservation).Property(x => x.VehicleDepartedById).CurrentValue = operatorId;
            db.Entry(reservation).Property(x => x.ClosedAtUtc).CurrentValue = departedAt;
            db.Entry(reservation).Property(x => x.ClosedById).CurrentValue = operatorId;
        }
        db.Reservations.Add(reservation);

        await db.SaveChangesAsync();

        var chargingEvent = new ChargingSessionCompletedEvent(
            SessionId: Guid.NewGuid(),
            ReservationId: reservationId,
            CustomerId: customerId,
            ChargingBayId: bayId,
            StartMeterKwh: 100.0m,
            EndMeterKwh: 110.501m,
            TotalKwh: 10.501m,
            StartedAtUtc: startedAt,
            CompletedAtUtc: completedAt,
            AppliedKwhRate: appliedRate);

        var payload = JsonSerializer.Serialize(chargingEvent);
        await billingConsumer.HandleAsync(
            "ChargingSessionCompleted",
            EventRoutingKeys.ChargingSessionCompleted,
            payload,
            Guid.NewGuid(),
            CancellationToken.None);

        var invoice = await db.Invoices.SingleOrDefaultAsync(x => x.ReservationId == reservationId);
        Assert.NotNull(invoice);
        Assert.Equal(10.501m, invoice.TotalKwh);
        Assert.Equal(expectedRate, invoice.KwhRate);
        Assert.Equal((decimal)expectedAmount, invoice.EnergyAmount);
        Assert.Equal(invoice.EnergyAmount, invoice.TotalAmount);
        Assert.Equal(InvoiceStatus.Pending, invoice.Status);

        // Verify that an outbox event InvoiceGenerated was also queued!
        var outboxEvent = await db.OutboxMessages.SingleOrDefaultAsync(x => x.RoutingKey == EventRoutingKeys.InvoiceGenerated);
        Assert.NotNull(outboxEvent);
        var invoiceEvent = JsonSerializer.Deserialize<InvoiceGeneratedEvent>(outboxEvent.Payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(invoiceEvent);
        Assert.Equal(invoice.TotalAmount, invoiceEvent.TotalAmount);
    }

    [Fact]
    public async Task NotificationConsumer_CreatesNotificationForReservationConfirmed()
    {
        await using var db = await CreateInMemoryDbContextAsync();
        var consumer = new NotificationConsumer(db, NullLogger<NotificationConsumer>.Instance, TimeProvider.System);

        var customerId = Guid.NewGuid();
        var customer = CreateTestUser(customerId, "notify_test@example.com");
        var operatorId = Guid.NewGuid();
        var otherOperator = Guid.NewGuid();
        var laterOperator = Guid.NewGuid();
        var station = new Location("Notification station", "Test", 10, 106, true, false);
        var otherStation = new Location("Other station", "Test", 11, 107, true, false);
        var now = DateTimeOffset.UtcNow;
        db.AddRange(customer, station, otherStation,
            CreateTestUser(operatorId, "operator@notice.test"),
            CreateTestUser(otherOperator, "other@notice.test"),
            CreateTestUser(laterOperator, "later@notice.test"),
            new OperatorAssignment { LocationId = station.Id, OperatorId = operatorId, AssignedAtUtc = now.AddHours(-1) },
            new OperatorAssignment { LocationId = otherStation.Id, OperatorId = otherOperator },
            new OperatorAssignment { LocationId = station.Id, OperatorId = laterOperator, AssignedAtUtc = now.AddHours(1) });
        await db.SaveChangesAsync();

        var confirmedEvent = new ReservationConfirmedEvent(
            ReservationId: Guid.NewGuid(),
            CustomerId: customerId,
            LocationId: station.Id,
            Code: "RES-9999",
            ServiceType: "Charging",
            ExpiresAtUtc: now.AddMinutes(15),
            CreatedAtUtc: now);

        var payload = JsonSerializer.Serialize(confirmedEvent);
        var eventId = Guid.NewGuid();
        await consumer.HandleAsync(
            "ReservationConfirmed",
            EventRoutingKeys.ReservationConfirmed,
            payload,
            eventId,
            CancellationToken.None);
        await consumer.HandleAsync("ReservationConfirmed", EventRoutingKeys.ReservationConfirmed,
            payload, eventId, CancellationToken.None);

        var notification = await db.Notifications.SingleOrDefaultAsync(x => x.UserId == customerId);
        Assert.NotNull(notification);
        Assert.Contains("RES-9999", notification.Message);
        Assert.Equal(NotificationType.Info, notification.Type);
        Assert.False(notification.IsRead);
        var operatorNotice = await db.Notifications.SingleAsync(x => x.UserId == operatorId);
        Assert.Equal($"/operator/dashboard?reservation={confirmedEvent.ReservationId:D}", operatorNotice.ActionUrl);
        Assert.False(operatorNotice.IsRead);
        Assert.Equal(2, await db.Notifications.CountAsync());
    }

    [Theory]
    [InlineData(ChargerState.Faulted, 5, true)]
    [InlineData(ChargerState.Faulted, 16, false)]
    [InlineData(ChargerState.Active, 5, false)]
    public async Task BayFault_NotifiesAffectedCustomerWithoutChangingReservation(
        ChargerState equipmentState, int minutesSinceFault, bool shouldNotify)
    {
        await using var db = await CreateInMemoryDbContextAsync();
        var faultedAt = new DateTimeOffset(2026, 11, 21, 2, 0, 0, TimeSpan.Zero);
        var consumer = new NotificationConsumer(db, NullLogger<NotificationConsumer>.Instance,
            new FixedClock(faultedAt.AddMinutes(minutesSinceFault)));

        var location = new Location("Station B", "Address B", 10.5m, 106.5m, true, true);
        var locationId = location.Id;
        var customerId = Guid.NewGuid();

        var customer = CreateTestUser(customerId, "reroute_test@example.com");
        db.Locations.Add(location);
        db.Users.Add(customer);

        var faultedBay = new ChargingBay
        {
            Id = Guid.NewGuid(),
            LocationId = locationId,
            Code = "BAY-01",
            PowerKw = 60,
            EquipmentState = equipmentState,
            ParkingState = ParkingState.Vacant
        };

        var availableBay = new ChargingBay
        {
            Id = Guid.NewGuid(),
            LocationId = locationId,
            Code = "BAY-02",
            PowerKw = 60,
            EquipmentState = ChargerState.Active,
            ParkingState = ParkingState.Vacant
        };

        db.ChargingBays.AddRange(faultedBay, availableBay);

        db.Reservations.Add(new Reservation
        {
            LocationId = locationId,
            CustomerId = customerId,
            ChargingBayId = faultedBay.Id,
            Code = "RES-FAULT",
            CreatedAtUtc = faultedAt.AddMinutes(-1),
            ExpiresAtUtc = faultedAt.AddMinutes(14)
        });

        await db.SaveChangesAsync();

        var faultEvent = new ChargingBayFaultedEvent(
            ChargingBayId: faultedBay.Id,
            LocationId: locationId,
            BayCode: "BAY-01",
            Reason: "Overcurrent trip",
            FaultedAtUtc: faultedAt);

        var payload = JsonSerializer.Serialize(faultEvent);
        await consumer.HandleAsync(
            "ChargingBayFaulted",
            EventRoutingKeys.BayFaulted,
            payload,
            Guid.NewGuid(),
            CancellationToken.None);

        var savedReservation = await db.Reservations.AsNoTracking().SingleAsync();
        Assert.Equal(faultedBay.Id, savedReservation.ChargingBayId);
        Assert.Equal(ReservationStatus.Held, savedReservation.Status);
        Assert.Empty(await db.OutboxMessages.ToListAsync());

        var notifications = await db.Notifications.ToListAsync();
        if (shouldNotify)
        {
            var notification = Assert.Single(notifications);
            Assert.Equal(customerId, notification.UserId);
            Assert.Contains(faultedBay.Code, notification.Message);
            Assert.Equal(NotificationType.Warning, notification.Type);
        }
        else
        {
            Assert.Empty(notifications);
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Theory]
    [InlineData(null, "Lượt đặt đã hủy do sự cố")]
    [InlineData("B-02", "Đã đổi tài nguyên giữ chỗ")]
    public async Task Incident_resolution_notifies_customer_once_on_redelivery(string? replacement, string title)
    {
        await using var db = await CreateInMemoryDbContextAsync();
        var customerId = Guid.NewGuid();
        db.Users.Add(CreateTestUser(customerId, "incident-notify@example.test"));
        await db.SaveChangesAsync();
        var consumer = new NotificationConsumer(db, NullLogger<NotificationConsumer>.Instance, TimeProvider.System);
        var payload = JsonSerializer.Serialize(new IncidentReservationResolvedEvent(customerId, replacement));
        var messageId = Guid.NewGuid();
        for (var i = 0; i < 2; i++)
            await consumer.HandleAsync("IncidentReservationResolved", EventRoutingKeys.IncidentReservationResolved,
                payload, messageId, default);

        var notification = await db.Notifications.SingleAsync();
        Assert.Equal(customerId, notification.UserId);
        Assert.Equal(title, notification.Title);
        if (replacement is not null) Assert.Contains(replacement, notification.Message);
    }
}
