using System.Security.Claims;
using EVChargeSwap.Common.Messaging;
using EVChargeSwap.Common.Messaging.Consumers;
using EVChargeSwap.Common.Messaging.Events;
using EVChargeSwap.Data;
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
        var hold = Assert.IsType<Reservation>((await holds.HoldAsync(customer.Id, station.Id,
            ReservationServiceType.BatterySwap, default)).Reservation);
        Assert.Equal(1, await holds.CheckInAsync(station.Id, op.Id, hold.Code, default));
        var request = new CompleteBatterySwapRequest(Guid.NewGuid().ToString("N"), issued.Serial, "Received for inspection");
        var endpoint = new BatterySwapsController(db, clock, outbox, new NoOpNotifier())
            { ControllerContext = Context(other.Id) };
        Assert.IsType<NotFoundResult>(await endpoint.Complete(hold.Id, request, default));
        endpoint.ControllerContext = Context(op.Id);
        var swap = Assert.IsType<BatterySwap>(Assert.IsType<OkObjectResult>(
            await endpoint.Complete(hold.Id, request, default)).Value);
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
        var first = Assert.IsType<Payment>(Assert.IsType<ObjectResult>(
            await Payment(x => x.Create(invoice.Id, default))).Value);
        var failure = new MockPaymentEvent(Guid.NewGuid(), first.Id, invoice.Id, first.Amount, false);
        Assert.IsType<UnauthorizedResult>(await Payment(x => x.Webhook(failure, "invalid", default)));
        var wrongAmount = failure with { Amount = first.Amount + 1m };
        Assert.Equal(409, Assert.IsType<ObjectResult>(await Payment(x => x.Webhook(
            wrongAmount, PaymentsController.Sign(wrongAmount, Secret), default))).StatusCode);
        Assert.IsType<NoContentResult>(await Payment(x => x.Webhook(failure, PaymentsController.Sign(failure, Secret), default)));
        var retry = Assert.IsType<Payment>(Assert.IsType<ObjectResult>(await Payment(x => x.Create(invoice.Id, default))).Value);
        Assert.NotEqual(first.Id, retry.Id);
        var success = new MockPaymentEvent(Guid.NewGuid(), retry.Id, invoice.Id, retry.Amount, true);
        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ =>
            Payment(x => x.Webhook(success, PaymentsController.Sign(success, Secret), default))));
        Assert.All(results, result => Assert.IsType<NoContentResult>(result));
        Assert.Equal(1, await db.Payments.CountAsync(x => x.InvoiceId == invoice.Id && x.Status == PaymentStatus.Succeeded));
        Assert.Equal(1, await db.PaymentWebhookEvents.CountAsync(x => x.PaymentId == retry.Id));
        Assert.Equal(InvoiceStatus.Paid, await db.Invoices.Where(x => x.Id == invoice.Id).Select(x => x.Status).SingleAsync());
    }

    private static ControllerContext Context(Guid id) => new()
    {
        HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", id.ToString())])) }
    };

    private static ApplicationUser User() => new($"{Guid.NewGuid():N}@example.test", "Billing test", DateTime.UtcNow);

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 16, 2, 0, 0, TimeSpan.Zero);
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
