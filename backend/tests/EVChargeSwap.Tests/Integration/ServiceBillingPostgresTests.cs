using System.Security.Claims;
using EVChargeSwap.Common.Messaging;
using EVChargeSwap.Common.Messaging.Consumers;
using EVChargeSwap.Common.Messaging.Events;
using EVChargeSwap.Data;
using EVChargeSwap.Features.Authorization;
using EVChargeSwap.Features.Locations.Entities;
using EVChargeSwap.Features.Locations.Services;
using EVChargeSwap.Features.Reservations;
using EVChargeSwap.Features.Reservations.Entities;
using EVChargeSwap.Features.Reservations.Services;
using EVChargeSwap.Features.Users.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EVChargeSwap.Tests.Integration;

public sealed class ServiceBillingPostgresTests
{
    private const string Secret = "test-webhook-key-at-least-thirty-two-characters";

    [ReservationPostgresFact]
    public async Task Station_price_summary_matches_billing_rates_for_the_assigned_station()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable("EV_RESERVATIONS_TEST_DATABASE")).Options;
        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync();
        var clock = new FixedClock();
        var station = new Location("Pricing station", "Test", 10, 106, true, true);
        var op = User();
        var other = User();
        db.AddRange(station, op, other, new OperatorAssignment { LocationId = station.Id, OperatorId = op.Id });
        await db.SaveChangesAsync();

        var endpoint = new TariffsController(db, clock) { ControllerContext = Context(op.Id, AppRoles.Operator) };
        async Task<CurrentStationTariffsResponse> Current() => Assert.IsType<CurrentStationTariffsResponse>(
            Assert.IsType<OkObjectResult>((await endpoint.Current(default)).Result).Value);

        Assert.Equal(new CurrentStationTariffsResponse(3858m, null), await Current());
        Assert.IsType<ObjectResult>(await endpoint.Create(new(ReservationServiceType.BatterySwap, 25000m, null), default));
        var later = clock.GetUtcNow().AddHours(1);
        Assert.IsType<ObjectResult>(await endpoint.Create(new(ReservationServiceType.BatterySwap, 30000m, later), default));
        Assert.Equal(new CurrentStationTariffsResponse(3858m, 25000m), await Current());
        clock.Now = later;
        Assert.Equal(new CurrentStationTariffsResponse(3858m, 30000m), await Current());

        endpoint.ControllerContext = Context(other.Id, AppRoles.Operator);
        Assert.IsType<NotFoundResult>((await endpoint.Current(default)).Result);
    }

    [ReservationPostgresFact]
    public async Task Swap_commits_inventory_and_invoice_then_signed_payment_can_retry_and_settle_once()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable("EV_RESERVATIONS_TEST_DATABASE")).Options;
        var clock = new FixedClock();
        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync();
        var station = new Location("Billing station", "Test", 10, 106, false, true);
        station.SetOperatingStatus(LocationOperatingStatus.Open);
        var customer = User();
        var op = User();
        var other = User();
        var issued = new Battery { LocationId = station.Id, Serial = Guid.NewGuid().ToString("N").ToUpperInvariant(), State = BatteryState.Ready };
        db.AddRange(station, customer, op, other, issued,
            new OperatorAssignment { LocationId = station.Id, OperatorId = op.Id },
            new ServiceHours { LocationId = station.Id, Day = DayOfWeek.Monday,
                ServiceType = ReservationServiceType.BatterySwap, OpensAtMinute = 0, ClosesAtMinute = 1440 },
            new Tariff { LocationId = station.Id, ServiceType = ReservationServiceType.BatterySwap,
                UnitPrice = 25000m, EffectiveFromUtc = clock.GetUtcNow().AddDays(-1), CreatedById = op.Id,
                CreatedAtUtc = clock.GetUtcNow().AddDays(-1) });
        await db.SaveChangesAsync();
        var outbox = new OutboxService(db, NullLogger<OutboxService>.Instance);
        var holds = new ReservationService(db, clock, outbox);
        var bookingKey = Guid.NewGuid();
        var hold = Assert.IsType<Reservation>((await holds.HoldAsync(customer.Id, station.Id,
            ReservationServiceType.BatterySwap, default, bookingKey)).Reservation);
        Assert.Equal(1, await holds.CheckInAsync(station.Id, op.Id, hold.Code, default));
        var request = new CompleteBatterySwapRequest(Guid.NewGuid().ToString("N"), issued.Serial, "Received for inspection");
        var endpoint = new BatterySwapsController(db, clock, outbox, new NoOpNotifier())
            { ControllerContext = Context(other.Id) };
        Assert.IsType<NotFoundResult>(await endpoint.Complete(hold.Id, request, default));
        endpoint.ControllerContext = Context(op.Id);
        var completion = Assert.IsType<BatterySwapCompletionResponse>(Assert.IsType<OkObjectResult>(
            await endpoint.Complete(hold.Id, request, default)).Value);
        var swap = completion;
        var replay = Assert.IsType<BatterySwapCompletionResponse>(Assert.IsType<OkObjectResult>(
            await endpoint.Complete(hold.Id, request, default)).Value);
        Assert.Equal(completion.Invoice.Id, replay.Invoice.Id);
        var returned = await db.Batteries.AsNoTracking().SingleAsync(x => x.Id == swap.ReturnedBatteryId);
        Assert.Equal(BatteryState.Inspection, returned.State);
        Assert.Equal(station.Id, returned.LocationId);
        Assert.Equal(BatteryState.WithCustomer, issued.State);
        Assert.Equal(customer.Id, issued.CustodianCustomerId);
        Assert.Null(issued.LocationId);
        Assert.Equal(ReservationStatus.Completed, await db.Reservations.Where(x => x.Id == hold.Id).Select(x => x.Status).SingleAsync());

        var completed = await db.OutboxMessages.SingleAsync(x => x.RoutingKey == EventRoutingKeys.BatterySwapCompleted &&
            x.Payload.Contains(hold.Id.ToString()));
        var billing = new BillingConsumer(db, outbox, NullLogger<BillingConsumer>.Instance);
        await billing.HandleAsync(completed.EventType, completed.RoutingKey, completed.Payload, completed.Id, default);
        var invoice = await db.Invoices.SingleAsync(x => x.ReservationId == hold.Id);
        Assert.Equal(25000m, invoice.TotalAmount);
        Assert.Equal(25000m, invoice.SwapRate);

        var nextBattery = new Battery { LocationId = station.Id, Serial = Guid.NewGuid().ToString("N").ToUpperInvariant(),
            State = BatteryState.Ready };
        db.Add(nextBattery);
        await db.SaveChangesAsync();

        async Task AssertBookingBlockedByInvoice()
        {
            var blocked = await holds.HoldAsync(customer.Id, station.Id, ReservationServiceType.BatterySwap, default);
            Assert.Null(blocked.Reservation);
            Assert.Contains("hóa đơn chưa thanh toán", blocked.Conflict!);
        }

        Assert.Null(invoice.DueAtUtc);
        await AssertBookingBlockedByInvoice();

        var checkout = new OperatorCheckoutController(db, clock, outbox) { ControllerContext = Context(other.Id) };
        Assert.IsType<NotFoundResult>(await checkout.Defer(invoice.Id, default));
        checkout.ControllerContext = Context(op.Id);
        Assert.IsType<NoContentResult>(await checkout.Defer(invoice.Id, default));
        var dueAt = clock.GetUtcNow().AddDays(7);
        Assert.Equal(dueAt, invoice.DueAtUtc);
        clock.Now = clock.Now.AddMinutes(1);
        Assert.IsType<NoContentResult>(await checkout.Defer(invoice.Id, default));
        Assert.Equal(dueAt, invoice.DueAtUtc);
        await AssertBookingBlockedByInvoice();

        clock.Now = dueAt;
        await AssertBookingBlockedByInvoice();
        var replayedHold = await holds.HoldAsync(customer.Id, station.Id, ReservationServiceType.BatterySwap, default, bookingKey);
        Assert.True(replayedHold.Replayed);
        Assert.Equal(hold.Id, replayedHold.Reservation!.Id);

        async Task<IActionResult> Payment(Func<PaymentsController, Task<IActionResult>> call)
        {
            await using var requestDb = new AppDbContext(options);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["MockPayments:WebhookSecret"] = Secret }).Build();
            var controller = new PaymentsController(requestDb, clock,
                new OutboxService(requestDb, NullLogger<OutboxService>.Instance), configuration, new DevEnvironment())
                { ControllerContext = Context(customer.Id) };
            return await call(controller);
        }
        var first = Assert.IsType<QrPaymentResponse>(Assert.IsType<ObjectResult>(
            await Payment(x => x.Create(invoice.Id, default))).Value);
        var failure = new MockPaymentEvent(Guid.NewGuid(), first.Id, invoice.Id, first.Amount, false);
        Assert.IsType<UnauthorizedResult>(await Payment(x => x.Webhook(failure, "invalid", default)));
        var wrongAmount = failure with { EventId = Guid.NewGuid(), Amount = first.Amount + 1m };
        Assert.Equal(409, Assert.IsType<ObjectResult>(await Payment(x => x.Webhook(
            wrongAmount, PaymentsController.Sign(wrongAmount, Secret), default))).StatusCode);
        var underpaid = failure with { EventId = Guid.NewGuid(), Amount = first.Amount - 1m, Succeeded = true };
        Assert.Equal(409, Assert.IsType<ObjectResult>(await Payment(x => x.Webhook(
            underpaid, PaymentsController.Sign(underpaid, Secret), default))).StatusCode);
        Assert.Equal(InvoiceStatus.Pending, await db.Invoices.Where(x => x.Id == invoice.Id).Select(x => x.Status).SingleAsync());
        Assert.IsType<NoContentResult>(await Payment(x => x.Webhook(failure, PaymentsController.Sign(failure, Secret), default)));
        await AssertBookingBlockedByInvoice();

        // Req #9: Publishing a new tariff must never alter the amount of already-completed swaps/invoices.
        var tariffEndpoint = new TariffsController(db, clock) { ControllerContext = Context(op.Id, AppRoles.Operator) };
        Assert.IsType<ObjectResult>(await tariffEndpoint.Create(new(ReservationServiceType.BatterySwap, 40000m, null), default));
        var unchangedInvoice = await db.Invoices.AsNoTracking().SingleAsync(x => x.Id == invoice.Id);
        Assert.Equal(25000m, unchangedInvoice.TotalAmount);
        Assert.Equal(25000m, unchangedInvoice.SwapRate);
        var retry = Assert.IsType<QrPaymentResponse>(Assert.IsType<ObjectResult>(await Payment(x => x.Create(invoice.Id, default))).Value);
        Assert.NotEqual(first.Id, retry.Id);
        var success = new MockPaymentEvent(Guid.NewGuid(), retry.Id, invoice.Id, retry.Amount, true);
        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ =>
            Payment(x => x.Webhook(success, PaymentsController.Sign(success, Secret), default))));
        Assert.All(results, result => Assert.IsType<NoContentResult>(result));
        Assert.Equal(1, await db.Payments.CountAsync(x => x.InvoiceId == invoice.Id && x.Status == PaymentStatus.Succeeded));
        Assert.Equal(1, await db.PaymentWebhookEvents.CountAsync(x => x.PaymentId == retry.Id));
        Assert.Equal(InvoiceStatus.Paid, await db.Invoices.Where(x => x.Id == invoice.Id).Select(x => x.Status).SingleAsync());
        Assert.Contains("540525000", first.QrPayload);
        Assert.Contains(first.Id.ToString("N")[..24].ToUpperInvariant(), first.QrPayload);

        async Task<Invoice> CompleteNextSwap(Battery battery)
        {
            var nextHold = Assert.IsType<Reservation>((await holds.HoldAsync(
                customer.Id, station.Id, ReservationServiceType.BatterySwap, default)).Reservation);
            Assert.Equal(1, await holds.CheckInAsync(station.Id, op.Id, nextHold.Code, default));
            var nextRequest = new CompleteBatterySwapRequest(Guid.NewGuid().ToString("N"), battery.Serial, "Checked");
            return Assert.IsType<BatterySwapCompletionResponse>(Assert.IsType<OkObjectResult>(
                await endpoint.Complete(nextHold.Id, nextRequest, default)).Value).Invoice;
        }

        var cashInvoice = await CompleteNextSwap(nextBattery);
        var pendingQr = Assert.IsType<QrPaymentResponse>(Assert.IsType<ObjectResult>(
            await Payment(x => x.Create(cashInvoice.Id, default))).Value);
        checkout.ControllerContext = Context(other.Id);
        Assert.IsType<NotFoundResult>(await checkout.CollectCash(cashInvoice.Id, default));
        checkout.ControllerContext = Context(op.Id);
        Assert.IsType<NoContentResult>(await checkout.CollectCash(cashInvoice.Id, default));
        Assert.IsType<NoContentResult>(await checkout.CollectCash(cashInvoice.Id, default));
        var queuedQr = new MockPaymentEvent(Guid.NewGuid(), pendingQr.Id, cashInvoice.Id, pendingQr.Amount, true);
        Assert.Equal(409, Assert.IsType<ObjectResult>(await Payment(x =>
            x.Webhook(queuedQr, PaymentsController.Sign(queuedQr, Secret), default))).StatusCode);
        var cash = await db.Payments.AsNoTracking().SingleAsync(x =>
            x.InvoiceId == cashInvoice.Id && x.Status == PaymentStatus.Succeeded);
        Assert.Equal(op.Id, cash.CollectedByOperatorId);

        var notifications = new NotificationConsumer(db, NullLogger<NotificationConsumer>.Instance, clock);
        foreach (var paidInvoice in new[] { invoice, cashInvoice })
        {
            var paidId = paidInvoice.Id.ToString();
            var paymentEvent = await db.OutboxMessages.SingleAsync(x =>
                x.RoutingKey == EventRoutingKeys.PaymentSucceeded && x.Payload.Contains(paidId));
            await notifications.HandleAsync(paymentEvent.EventType, paymentEvent.RoutingKey,
                paymentEvent.Payload, paymentEvent.Id, default);
        }
        var qrNotice = Assert.Single(await db.Notifications.Where(x => x.UserId == op.Id).ToListAsync());
        Assert.Equal($"/operator/dashboard?invoice={invoice.Id:D}", qrNotice.ActionUrl);

        var stationHistory = new InvoicesController(db) { ControllerContext = Context(op.Id, AppRoles.Operator) };
        var history = await stationHistory.GetInvoices(default);
        var rows = Assert.IsAssignableFrom<IReadOnlyList<InvoiceDetailResponse>>(
            Assert.IsType<OkObjectResult>(history.Result).Value);
        Assert.Contains(rows, x => x.Id == cashInvoice.Id && x.PaymentMethod == "Cash" && x.CollectedByOperatorId == op.Id);
        stationHistory.ControllerContext = Context(other.Id, AppRoles.Operator);
        var otherStationDetail = await stationHistory.GetInvoice(cashInvoice.Id, default);
        Assert.IsType<NotFoundResult>(otherStationDetail.Result);

        var raceBattery = new Battery { LocationId = station.Id, Serial = Guid.NewGuid().ToString("N").ToUpperInvariant(),
            State = BatteryState.Ready };
        db.Add(raceBattery);
        await db.SaveChangesAsync();
        var raceInvoice = await CompleteNextSwap(raceBattery);
        var raceQr = Assert.IsType<QrPaymentResponse>(Assert.IsType<ObjectResult>(
            await Payment(x => x.Create(raceInvoice.Id, default))).Value);
        var raceEvent = new MockPaymentEvent(Guid.NewGuid(), raceQr.Id, raceInvoice.Id, raceQr.Amount, true);

        async Task<IActionResult> CollectConcurrently()
        {
            await using var cashDb = new AppDbContext(options);
            var cashEndpoint = new OperatorCheckoutController(cashDb, clock,
                new OutboxService(cashDb, NullLogger<OutboxService>.Instance)) { ControllerContext = Context(op.Id) };
            return await cashEndpoint.CollectCash(raceInvoice.Id, default);
        }

        var race = await Task.WhenAll(CollectConcurrently(),
            Payment(x => x.Webhook(raceEvent, PaymentsController.Sign(raceEvent, Secret), default)));
        Assert.Single(race.OfType<NoContentResult>());
        Assert.Equal(409, Assert.Single(race.OfType<ObjectResult>()).StatusCode);
        Assert.Equal(1, await db.Payments.CountAsync(x => x.InvoiceId == raceInvoice.Id && x.Status == PaymentStatus.Succeeded));
    }

    private static ControllerContext Context(Guid id, string role = AppRoles.Customer) => new()
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", id.ToString()), new Claim(ClaimTypes.Role, role)]))
        }
    };

    private static ApplicationUser User()
    {
        var email = $"{Guid.NewGuid():N}@example.test";
        return new ApplicationUser(email, "Billing test", DateTime.UtcNow)
        {
            NormalizedEmail = email.ToUpperInvariant(),
            NormalizedUserName = email.ToUpperInvariant()
        };
    }

    private sealed class FixedClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 16, 2, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class NoOpNotifier : IStationNotifier
    {
        public Task NotifyStationInventoryChangedAsync(Guid locationId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class DevEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "EVChargeSwap.Tests";
        public string WebRootPath { get; set; } = "";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
