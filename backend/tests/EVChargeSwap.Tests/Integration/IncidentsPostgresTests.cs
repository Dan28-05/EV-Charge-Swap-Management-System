using System.Security.Claims;
using System.Text.Json;
using EVChargeSwap.Common.Messaging;
using EVChargeSwap.Common.Messaging.Consumers;
using EVChargeSwap.Common.Messaging.Events;
using EVChargeSwap.Data;
using EVChargeSwap.Features.Locations.Caching;
using EVChargeSwap.Features.Locations.Entities;
using EVChargeSwap.Features.Reservations;
using EVChargeSwap.Features.Reservations.Entities;
using EVChargeSwap.Features.Reservations.Services;
using EVChargeSwap.Features.Users.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EVChargeSwap.Tests.Integration;

public sealed class IncidentsPostgresTests
{
    [ReservationPostgresFact]
    public async Task Operator_resolves_faults_at_assigned_station_and_preserves_occupied_bay()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable("EV_RESERVATIONS_TEST_DATABASE")).Options;
        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync();
        var clock = new IncidentClock();
        var station = new Location("Incident station", "Incident address", 10, 106, true, true);
        station.SetOperatingStatus(LocationOperatingStatus.Open);
        var customer = NewUser();
        var competingCustomer = NewUser();
        var operatorUser = NewUser();
        var otherOperator = NewUser();
        var originalBay = new ChargingBay { LocationId = station.Id, Code = "A", PowerKw = 60 };
        var spareBay = new ChargingBay { LocationId = station.Id, Code = "B", PowerKw = 60 };
        var battery = new Battery { LocationId = station.Id, Serial = Guid.NewGuid().ToString(), State = BatteryState.Ready };
        db.AddRange(station, customer, competingCustomer, operatorUser, otherOperator, originalBay, spareBay, battery,
            new OperatorAssignment { LocationId = station.Id, OperatorId = operatorUser.Id },
            new ServiceHours { LocationId = station.Id, Day = DayOfWeek.Monday,
                ServiceType = ReservationServiceType.Charging, OpensAtMinute = 0, ClosesAtMinute = 1440 },
            new ServiceHours { LocationId = station.Id, Day = DayOfWeek.Monday,
                ServiceType = ReservationServiceType.BatterySwap, OpensAtMinute = 0, ClosesAtMinute = 1440 });
        await db.SaveChangesAsync();

        var outbox = new OutboxService(db, NullLogger<OutboxService>.Instance);
        var holds = new ReservationService(db, clock, outbox);
        var incidents = new IncidentService(db, clock, outbox, holds);
        var operations = new OperatorOperationsController(db, clock, new NoOpLocationCache(), outbox: outbox)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", operatorUser.Id.ToString())]))
                }
            }
        };
        var held = (await holds.HoldAsync(customer.Id, station.Id, ReservationServiceType.Charging, default)).Reservation!;
        Assert.Equal(originalBay.Id, held.ChargingBayId);
        Assert.IsType<NoContentResult>(await operations.ChangeBay(originalBay.Id,
            new ChangeBayStateRequest(ChargerState.Faulted, "Mất nguồn"), default));
        Assert.IsType<NoContentResult>(await operations.ChangeBay(originalBay.Id,
            new ChangeBayStateRequest(ChargerState.Faulted, "Mất nguồn"), default));
        var incident = await db.IncidentReports.AsNoTracking().SingleAsync(x => x.ReservationId == held.Id);
        Assert.Equal("Mất nguồn", incident.Description);
        async Task<JsonElement> DashboardIncident()
        {
            var result = Assert.IsType<OkObjectResult>(await operations.Station(default));
            return JsonSerializer.SerializeToElement(result.Value).GetProperty("reservations")
                .EnumerateArray().Single(row => row.GetProperty("Id").GetGuid() == held.Id)
                .GetProperty("Incident");
        }
        Assert.Equal(IncidentResolution.Reassigned, incident.Resolution);
        Assert.Equal(spareBay.Id, incident.ReplacementChargingBayId);
        Assert.Null(await incidents.GetOptionsAsync(incident.Id, otherOperator.Id, default));
        Assert.Equal(station.Id, (await incidents.ResolveAsync(incident.Id, operatorUser.Id,
            IncidentResolution.Reassigned, spareBay.Id, default)).LocationId);
        var moved = await db.Reservations.AsNoTracking().SingleAsync(x => x.Id == held.Id);
        Assert.Equal(JsonValueKind.Null, (await DashboardIncident()).ValueKind);
        Assert.Equal(spareBay.Id, moved.ChargingBayId);
        Assert.Equal(held.ExpiresAtUtc, moved.ExpiresAtUtc);
        Assert.Equal(held.Code, moved.Code);
        Assert.Equal(1, await holds.CheckInAsync(station.Id, operatorUser.Id, moved.Code, default));

        db.ChangeTracker.Clear();
        var charging = new ChargingService(db, clock, outbox);
        Assert.True(await charging.VehicleArrivedAsync(held.Id, operatorUser.Id, default));
        var session = await charging.StartAsync(held.Id, operatorUser.Id, 10m, default);
        Assert.NotNull(session);
        Assert.IsType<NoContentResult>(await operations.ChangeBay(spareBay.Id,
            new ChangeBayStateRequest(ChargerState.Faulted, "Dừng cấp điện"), default));
        var occupied = await db.IncidentReports.AsNoTracking().SingleAsync(x => x.ChargingBayId == spareBay.Id);
        Assert.Equal(session.Id, occupied.ChargingSessionId);
        Assert.False((await incidents.GetOptionsAsync(occupied.Id, operatorUser.Id, default))!.CanChangeReservation);
        Assert.NotNull((await incidents.ResolveAsync(occupied.Id, operatorUser.Id,
            IncidentResolution.Cancelled, null, default)).Conflict);
        Assert.Equal(ParkingState.Occupied, (await db.ChargingBays.AsNoTracking().SingleAsync(x => x.Id == spareBay.Id)).ParkingState);
        Assert.True(await charging.CompleteAsync(held.Id, operatorUser.Id, 11m, default));
        Assert.True(await charging.VehicleDepartedAsync(held.Id, operatorUser.Id, default));

        var swap = Assert.IsType<Reservation>((await holds.HoldAsync(
            competingCustomer.Id, station.Id, ReservationServiceType.BatterySwap, default)).Reservation);
        Assert.IsType<NoContentResult>(await operations.ChangeBattery(battery.Id,
            new ChangeBatteryStateRequest(BatteryState.Faulted, "Pin không đạt kiểm tra"), default));
        var batteryIncident = await db.IncidentReports.AsNoTracking().SingleAsync(x => x.ReservationId == swap.Id);
        Assert.Equal(IncidentResolution.Cancelled, batteryIncident.Resolution);
        var cancelled = await db.Reservations.AsNoTracking().SingleAsync(x => x.Id == swap.Id);
        Assert.Equal(ReservationStatus.Cancelled, cancelled.Status);
        Assert.Equal(IncidentService.FaultCancellationReason, cancelled.ClosingReason);
        var swapId = swap.Id.ToString();
        var cancellationEvent = await db.OutboxMessages.SingleAsync(x =>
            x.RoutingKey == EventRoutingKeys.IncidentReservationResolved && x.Payload.Contains(swapId));
        var notifications = new NotificationConsumer(db, NullLogger<NotificationConsumer>.Instance, clock);
        await notifications.HandleAsync(cancellationEvent.EventType, cancellationEvent.RoutingKey,
            cancellationEvent.Payload, cancellationEvent.Id, default);
        var notice = Assert.Single(await db.Notifications.Where(x => x.UserId == competingCustomer.Id).ToListAsync());
        Assert.Equal(competingCustomer.Id, notice.UserId);
        Assert.Contains("đã hủy", notice.Title);
    }

    private static ApplicationUser NewUser()
    {
        var email = $"{Guid.NewGuid():N}@example.test";
        return new ApplicationUser(email, "Incident test", DateTime.UtcNow)
        {
            NormalizedEmail = email.ToUpperInvariant(), NormalizedUserName = email.ToUpperInvariant()
        };
    }

    private sealed class IncidentClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 16, 2, 0, 0, TimeSpan.Zero);
    }
}
