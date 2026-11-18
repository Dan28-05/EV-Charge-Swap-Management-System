using EVChargeSwap.Data;
using EVChargeSwap.Features.Locations.Entities;
using EVChargeSwap.Features.Reservations.Entities;
using EVChargeSwap.Features.Reservations.Services;
using EVChargeSwap.Features.Reservations;
using EVChargeSwap.Features.Locations.Caching;
using EVChargeSwap.Features.Authorization;
using EVChargeSwap.Features.Users.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Xunit;

namespace EVChargeSwap.Tests.Integration;

public sealed class ReservationPostgresFactAttribute : FactAttribute
{
    public ReservationPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("EV_RESERVATIONS_TEST_DATABASE")))
            Skip = "Set EV_RESERVATIONS_TEST_DATABASE to a disposable PostgreSQL database.";
    }
}

public sealed class ReservationsPostgresTests
{
    [ReservationPostgresFact]
    public async Task Charging_lifecycle_keeps_bay_occupied_until_operator_confirms_departure()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable("EV_RESERVATIONS_TEST_DATABASE")).Options;
        var clock = new BookingClock(new DateTimeOffset(2026, 11, 16, 2, 0, 0, TimeSpan.Zero));
        await using var setup = new AppDbContext(options);
        await setup.Database.MigrateAsync();
        var location = new Location("Charging flow", "Test address", 10, 106, true, false);
        location.SetOperatingStatus(LocationOperatingStatus.Open);
        var customer = NewUser();
        var nextCustomer = NewUser();
        var op = NewUser();
        var wrongOp = NewUser();
        var bay = new ChargingBay { LocationId = location.Id, Code = "A01", PowerKw = 60 };
        setup.AddRange(location, customer, nextCustomer, op, wrongOp, bay,
            new OperatorAssignment { LocationId = location.Id, OperatorId = op.Id },
            new ServiceHours { LocationId = location.Id, Day = DayOfWeek.Monday,
                ServiceType = ReservationServiceType.Charging, OpensAtMinute = 480, ClosesAtMinute = 1200 });
        await setup.SaveChangesAsync();

        async Task<T> WithDb<T>(Func<AppDbContext, Task<T>> action)
        {
            await using var requestDb = new AppDbContext(options);
            return await action(requestDb);
        }

        var held = (await WithDb(db => new ReservationService(db, clock)
            .HoldAsync(customer.Id, location.Id, ReservationServiceType.Charging, default))).Reservation!;
        Assert.Equal(1, await WithDb(db => new ReservationService(db, clock)
            .CheckInAsync(location.Id, op.Id, held.Code, default)));
        Assert.False(await WithDb(db => new ChargingService(db, clock)
            .VehicleArrivedAsync(held.Id, wrongOp.Id, default)));
        Assert.Null(await WithDb(db => new ChargingService(db, clock)
            .StartAsync(held.Id, op.Id, 10m, default)));
        Assert.True(await WithDb(db => new ChargingService(db, clock)
            .VehicleArrivedAsync(held.Id, op.Id, default)));
        Assert.Equal(ParkingState.Occupied, await WithDb(db => db.Set<ChargingBay>()
            .Where(x => x.Id == bay.Id).Select(x => x.ParkingState).SingleAsync()));
        Assert.Equal(0, await WithDb(db => new ReservationService(db, clock)
            .CloseWithoutServiceAsync(location.Id, op.Id, held.Id, default)));
        Assert.False(await WithDb(db => new ChargingService(db, clock)
            .VehicleDepartedAsync(held.Id, op.Id, default)));

        var session = await WithDb(db => new ChargingService(db, clock).StartAsync(held.Id, op.Id, 10m, default));
        Assert.NotNull(session);
        await using (var viewDb = new AppDbContext(options))
        {
            var controller = new ChargingController(viewDb, new ChargingService(viewDb, clock))
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(
                            [new Claim(JwtRegisteredClaimNames.Sub, op.Id.ToString())]))
                    }
                }
            };
            var view = await controller.Get(held.Id, default);
            var status = Assert.IsType<ChargingReservationStatusResponse>(Assert.IsType<OkObjectResult>(view.Result).Value);
            Assert.NotNull(status.Session);
            Assert.NotNull(status.VehicleArrivedAtUtc);
            controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(JwtRegisteredClaimNames.Sub, wrongOp.Id.ToString())]));
            Assert.IsType<NotFoundResult>((await controller.Get(held.Id, default)).Result);
        }
        Assert.Null(await WithDb(db => new ChargingService(db, clock).StartAsync(held.Id, op.Id, 10m, default)));
        Assert.False(await WithDb(db => new ChargingService(db, clock).ConfirmNoChargeAsync(held.Id, op.Id, default)));
        Assert.False(await WithDb(db => new ChargingService(db, clock).CompleteAsync(held.Id, op.Id, 9m, default)));
        Assert.False(await WithDb(db => new ChargingService(db, clock).CompleteAsync(held.Id, op.Id, 10.0009m, default)));
        Assert.True(await WithDb(db => new ChargingService(db, clock).CompleteAsync(held.Id, op.Id, 15.5m, default)));
        Assert.False(await WithDb(db => new ChargingService(db, clock).CompleteAsync(held.Id, op.Id, 16m, default)));
        Assert.Equal(ParkingState.Occupied, await WithDb(db => db.Set<ChargingBay>()
            .Where(x => x.Id == bay.Id).Select(x => x.ParkingState).SingleAsync()));
        Assert.Null((await WithDb(db => new ReservationService(db, clock)
            .HoldAsync(nextCustomer.Id, location.Id, ReservationServiceType.Charging, default))).Reservation);
        Assert.True(await WithDb(db => new ChargingService(db, clock).VehicleDepartedAsync(held.Id, op.Id, default)));
        Assert.False(await WithDb(db => new ChargingService(db, clock).VehicleDepartedAsync(held.Id, op.Id, default)));
        Assert.Equal(ParkingState.Vacant, await WithDb(db => db.Set<ChargingBay>()
            .Where(x => x.Id == bay.Id).Select(x => x.ParkingState).SingleAsync()));
        Assert.Equal(ReservationStatus.Completed, await WithDb(db => db.Set<Reservation>()
            .Where(x => x.Id == held.Id).Select(x => x.Status).SingleAsync()));

        var noCharge = (await WithDb(db => new ReservationService(db, clock)
            .HoldAsync(nextCustomer.Id, location.Id, ReservationServiceType.Charging, default))).Reservation!;
        Assert.Equal(1, await WithDb(db => new ReservationService(db, clock)
            .CheckInAsync(location.Id, op.Id, noCharge.Code, default)));
        Assert.True(await WithDb(db => new ChargingService(db, clock).VehicleArrivedAsync(noCharge.Id, op.Id, default)));
        Assert.True(await WithDb(db => new ChargingService(db, clock).ConfirmNoChargeAsync(noCharge.Id, op.Id, default)));
        Assert.Null(await WithDb(db => new ChargingService(db, clock).StartAsync(noCharge.Id, op.Id, 20m, default)));
        Assert.True(await WithDb(db => new ChargingService(db, clock).VehicleDepartedAsync(noCharge.Id, op.Id, default)));
        Assert.Equal("DID_NOT_CHARGE", await WithDb(db => db.Set<Reservation>()
            .Where(x => x.Id == noCharge.Id).Select(x => x.ClosingReason).SingleAsync()));
        Assert.False(await WithDb(db => db.Set<ChargingSession>()
            .AnyAsync(x => x.ReservationId == noCharge.Id)));
    }

    [ReservationPostgresFact]
    public async Task Migration_and_reservations_enforce_concurrency_expiry_and_operator_scope()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable("EV_RESERVATIONS_TEST_DATABASE")).Options;
        var clock = new BookingClock(new DateTimeOffset(2026, 11, 16, 2, 0, 0, TimeSpan.Zero));
        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync();

        var location = new Location("Booking test", "Test address", 10, 106, true, true);
        location.SetOperatingStatus(LocationOperatingStatus.Open);
        var otherLocation = new Location("Other location", "Another address", 10, 106, true, false);
        otherLocation.SetOperatingStatus(LocationOperatingStatus.Open);

        var firstCustomer = NewUser();
        var secondCustomer = NewUser();
        var operatorUser = NewUser();
        var otherOperator = NewUser();
        db.AddRange(location, otherLocation, firstCustomer, secondCustomer, operatorUser, otherOperator);
        var bay = new ChargingBay { LocationId = location.Id, Code = "A01", PowerKw = 60 };
        db.AddRange(
            bay,
            new ChargingBay { LocationId = otherLocation.Id, Code = "B01", PowerKw = 60 },
            new Battery { LocationId = location.Id, Serial = Guid.NewGuid().ToString(), State = BatteryState.Ready },
            new ServiceHours { LocationId = location.Id, Day = DayOfWeek.Monday,
                ServiceType = ReservationServiceType.Charging, OpensAtMinute = 480, ClosesAtMinute = 1200 },
            new ServiceHours { LocationId = otherLocation.Id, Day = DayOfWeek.Monday,
                ServiceType = ReservationServiceType.Charging, OpensAtMinute = 480, ClosesAtMinute = 1200 },
            new ServiceHours { LocationId = location.Id, Day = DayOfWeek.Monday,
                ServiceType = ReservationServiceType.BatterySwap, OpensAtMinute = 480, ClosesAtMinute = 1200 },
            new OperatorAssignment { LocationId = location.Id, OperatorId = operatorUser.Id },
            new OperatorAssignment { LocationId = otherLocation.Id, OperatorId = otherOperator.Id });
        await db.SaveChangesAsync();
        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = operatorUser.Id, RoleId = AppRoles.OperatorId });
        await db.SaveChangesAsync();

        async Task<(Reservation? Reservation, string? Conflict)> Hold(Guid userId, Guid stationId, ReservationServiceType type)
        {
            await using var requestDb = new AppDbContext(options);
            return await new ReservationService(requestDb, clock).HoldAsync(userId, stationId, type, default);
        }

        var charging = ReservationServiceType.Charging;
        var batterySwap = ReservationServiceType.BatterySwap;
        var competingBays = await Task.WhenAll(Hold(firstCustomer.Id, location.Id, charging),
            Hold(secondCustomer.Id, location.Id, charging));
        var bayHold = Assert.Single(competingBays, x => x.Reservation != null).Reservation!;
        Assert.Single(competingBays, x => x.Conflict != null);

        var service = new ReservationService(db, clock);
        Assert.Equal(0, await service.CheckInAsync(location.Id, otherOperator.Id, bayHold.Code, default));
        Assert.Equal(1, await service.CheckInAsync(location.Id, operatorUser.Id, bayHold.Code, default));
        Assert.Equal(0, await service.CheckInAsync(location.Id, operatorUser.Id, bayHold.Code, default));
        Assert.Equal(0, await service.CancelAsync(bayHold.CustomerId, bayHold.Id, default));

        clock.Now = bayHold.ExpiresAtUtc;
        await service.ExpireAsync(default);
        Assert.Equal(ReservationStatus.CheckedIn,
            await db.Set<Reservation>().Where(x => x.Id == bayHold.Id).Select(x => x.Status).SingleAsync());
        Assert.Equal(1, await service.CloseWithoutServiceAsync(location.Id, operatorUser.Id, bayHold.Id, default));
        Assert.Equal("DID_NOT_CHARGE", await db.Set<Reservation>().Where(x => x.Id == bayHold.Id)
            .Select(x => x.ClosingReason).SingleAsync());

        var competingCustomers = await Task.WhenAll(Hold(firstCustomer.Id, location.Id, charging),
            Hold(firstCustomer.Id, otherLocation.Id, charging));
        var customerHold = Assert.Single(competingCustomers, x => x.Reservation != null).Reservation!;
        Assert.Single(competingCustomers, x => x.Conflict != null);
        Assert.Equal(0, await service.CancelAsync(secondCustomer.Id, customerHold.Id, default));
        Assert.Equal(1, await service.CancelAsync(firstCustomer.Id, customerHold.Id, default));

        var competingBatteries = await Task.WhenAll(Hold(firstCustomer.Id, location.Id, batterySwap),
            Hold(secondCustomer.Id, location.Id, batterySwap));
        var batteryHold = Assert.Single(competingBatteries, x => x.Reservation != null).Reservation!;
        Assert.NotNull(batteryHold.BatteryId);
        Assert.Single(competingBatteries, x => x.Conflict != null);

        clock.Now = batteryHold.ExpiresAtUtc;
        Assert.Equal(0, await service.CheckInAsync(location.Id, operatorUser.Id, batteryHold.Code, default));
        await service.ExpireAsync(default);
        Assert.Equal(0, await service.ExpireAsync(default));
        Assert.Equal(ReservationStatus.Expired,
            await db.Set<Reservation>().Where(x => x.Id == batteryHold.Id).Select(x => x.Status).SingleAsync());
        var newBatteryHold = (await Hold(batteryHold.CustomerId, location.Id, batterySwap)).Reservation;
        Assert.NotNull(newBatteryHold);

        var operations = new OperatorOperationsController(db, clock, new NoOpLocationCache())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(JwtRegisteredClaimNames.Sub, operatorUser.Id.ToString())]))
                }
            }
        };
        Assert.IsType<NoContentResult>(await operations.ChangeBay(bay.Id,
            new ChangeBayStateRequest(ChargerState.Faulted), default));
        Assert.Equal(ChargerState.Faulted, await db.Set<ChargingBay>().AsNoTracking()
            .Where(x => x.Id == bay.Id).Select(x => x.EquipmentState).SingleAsync());
        Assert.Equal(operatorUser.Id, await db.Set<ChargingBay>().AsNoTracking()
            .Where(x => x.Id == bay.Id).Select(x => x.StateUpdatedById).SingleAsync());
        Assert.IsType<NoContentResult>(await operations.ChangeBattery(newBatteryHold.BatteryId!.Value,
            new ChangeBatteryStateRequest(BatteryState.Faulted), default));
        Assert.Equal(0, await service.CheckInAsync(location.Id, operatorUser.Id, newBatteryHold.Code, default));
        Assert.IsType<BadRequestObjectResult>(await operations.ReplaceHours(new ReplaceHoursRequest(
            ReservationServiceType.Charging, DayOfWeek.Monday,
            [new ServiceIntervalRequest(480, 700), new ServiceIntervalRequest(600, 800)]), default));
        Assert.IsType<NoContentResult>(await operations.ReplaceHours(new ReplaceHoursRequest(
            ReservationServiceType.Charging, DayOfWeek.Monday,
            [new ServiceIntervalRequest(540, 1080)]), default));
        Assert.Equal(540, await db.Set<ServiceHours>().AsNoTracking()
            .Where(x => x.LocationId == location.Id && x.ServiceType == ReservationServiceType.Charging)
            .Select(x => x.OpensAtMinute).SingleAsync());

        var assignmentService = new OperatorAssignmentService(db, clock);
        Assert.NotNull((await assignmentService.AssignAsync(operatorUser.Id, otherLocation.Id,
            otherOperator.Id, false, default)).Error);
        var moved = await assignmentService.AssignAsync(operatorUser.Id, otherLocation.Id,
            otherOperator.Id, true, default);
        Assert.NotNull(moved.Assignment);
        Assert.Equal(1, await db.Set<OperatorAssignment>().AsNoTracking()
            .CountAsync(x => x.OperatorId == operatorUser.Id && x.EndedAtUtc == null));
        Assert.Equal(2, await db.Set<OperatorAssignment>().AsNoTracking()
            .CountAsync(x => x.OperatorId == operatorUser.Id));
        Assert.Equal(1, await assignmentService.EndAsync(operatorUser.Id, default));
        Assert.Equal(0, await assignmentService.EndAsync(operatorUser.Id, default));
        Assert.False(await db.Set<OperatorAssignment>().AsNoTracking()
            .AnyAsync(x => x.OperatorId == operatorUser.Id && x.EndedAtUtc == null));
    }

    private static ApplicationUser NewUser()
    {
        var email = $"{Guid.NewGuid():N}@example.test";
        return new ApplicationUser(email, "Reservation test", DateTime.UtcNow)
        {
            NormalizedEmail = email.ToUpperInvariant(),
            NormalizedUserName = email.ToUpperInvariant()
        };
    }

    private sealed class BookingClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
