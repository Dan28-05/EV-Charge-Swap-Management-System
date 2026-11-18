using System.Security.Cryptography;
using EVChargeSwap.Data;
using EVChargeSwap.Features.Locations.Entities;
using EVChargeSwap.Features.Reservations.Entities;
using EVChargeSwap.Features.Reservations.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EVChargeSwap.Features.Reservations.Services;

public sealed class ReservationService(AppDbContext db, TimeProvider clock)
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public IQueryable<Reservation> Active => db.Set<Reservation>()
        .Where(x => x.Status == ReservationStatus.Held || x.Status == ReservationStatus.CheckedIn);

    public Task<int> ExpireAsync(CancellationToken ct) => ExpireAsync(db.Set<Reservation>(), ct);

    public async Task<(Reservation? Reservation, string? Conflict)> HoldAsync(
        Guid customerId, Guid locationId, ReservationServiceType serviceType, CancellationToken ct)
    {
        // Process elapsed holds even when the periodic job is delayed.
        await ExpireAsync(db.Set<Reservation>().Where(x => x.CustomerId == customerId || x.LocationId == locationId), ct);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var location = await db.Locations
            .FromSqlInterpolated($"SELECT * FROM locations WHERE id = {locationId} FOR UPDATE")
            .AsNoTracking().SingleOrDefaultAsync(ct);

        if (location is null || !location.IsActive)
            return (null, null);

        var now = clock.GetUtcNow();
        var local = now.ToOffset(VietnamOffset);
        var minute = local.Hour * 60 + local.Minute;
        var offersService = serviceType == ReservationServiceType.Charging
            ? location.OffersCarCharging
            : location.OffersBatterySwap;

        if (!offersService || location.OperatingStatus != LocationOperatingStatus.Open ||
            !await db.Set<ServiceHours>().AnyAsync(x => x.LocationId == locationId &&
                x.ServiceType == serviceType && x.Day == local.DayOfWeek &&
                x.OpensAtMinute <= minute && minute < x.ClosesAtMinute, ct))
            return (null, "The requested service is not open for booking.");

        if (await Active.AnyAsync(x => x.CustomerId == customerId, ct))
            return (null, "You already have an active reservation.");

        Guid? bayId = null;
        Guid? batteryId = null;
        if (serviceType == ReservationServiceType.Charging)
        {
            bayId = await db.Set<ChargingBay>()
                .Where(x => x.LocationId == locationId && x.EquipmentState == ChargerState.Active &&
                    x.ParkingState == ParkingState.Vacant && !Active.Any(r => r.ChargingBayId == x.Id))
                .OrderBy(x => x.Code).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
        }
        else
        {
            batteryId = await db.Set<Battery>()
                .Where(x => x.LocationId == locationId && x.State == BatteryState.Ready &&
                    !Active.Any(r => r.BatteryId == x.Id))
                .OrderBy(x => x.Serial).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
        }

        if (bayId is null && batteryId is null)
            return (null, "No resource is available for the requested service.");

        var reservation = new Reservation
        {
            CustomerId = customerId,
            LocationId = locationId,
            ChargingBayId = bayId,
            BatteryId = batteryId,
            Code = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(15)
        };

        db.Add(reservation);
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return (reservation, null);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: ReservationConfiguration.CustomerHoldIndex or
                ReservationConfiguration.BayHoldIndex or ReservationConfiguration.BatteryHoldIndex
        })
        {
            return (null, "An active reservation already holds the customer or resource.");
        }
    }

    public Task<int> CancelAsync(Guid customerId, Guid id, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        return db.Set<Reservation>().Where(x => x.Id == id && x.CustomerId == customerId &&
                x.Status == ReservationStatus.Held && x.ExpiresAtUtc > now)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.Status, ReservationStatus.Cancelled), ct);
    }

    public Task<int> CheckInAsync(Guid locationId, Guid operatorId, string code, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        return db.Set<Reservation>().Where(x => x.LocationId == locationId && x.Code == code &&
                x.Status == ReservationStatus.Held && x.ExpiresAtUtc > now &&
                (x.ChargingBayId != null && db.Set<ChargingBay>().Any(b => b.Id == x.ChargingBayId &&
                    b.EquipmentState == ChargerState.Active) ||
                 x.BatteryId != null && db.Set<Battery>().Any(b => b.Id == x.BatteryId && b.State == BatteryState.Ready)) &&
                db.Set<OperatorAssignment>().Any(a => a.LocationId == locationId && a.OperatorId == operatorId && a.EndedAtUtc == null))
            .ExecuteUpdateAsync(update => update
                .SetProperty(x => x.Status, ReservationStatus.CheckedIn)
                .SetProperty(x => x.CheckedInAtUtc, now)
                .SetProperty(x => x.CheckedInById, operatorId), ct);
    }

    // Called only after the operator confirms there was no service and the bay is clear.
    public Task<int> CloseWithoutServiceAsync(Guid locationId, Guid operatorId, Guid reservationId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        return db.Set<Reservation>().Where(x => x.Id == reservationId && x.LocationId == locationId &&
                x.Status == ReservationStatus.CheckedIn &&
                db.Set<OperatorAssignment>().Any(a => a.LocationId == locationId && a.OperatorId == operatorId && a.EndedAtUtc == null) &&
                (x.ChargingBayId == null || x.VehicleArrivedAtUtc == null &&
                    !db.Set<ChargingSession>().Any(s => s.ReservationId == x.Id) &&
                    db.Set<ChargingBay>().Any(b => b.Id == x.ChargingBayId &&
                    b.ParkingState == ParkingState.Vacant)))
            .ExecuteUpdateAsync(update => update
                .SetProperty(x => x.Status, ReservationStatus.Completed)
                .SetProperty(x => x.ClosingReason, x => x.ChargingBayId != null ? "DID_NOT_CHARGE" : "DID_NOT_SWAP")
                .SetProperty(x => x.ClosedById, operatorId)
                .SetProperty(x => x.ClosedAtUtc, now), ct);
    }

    private Task<int> ExpireAsync(IQueryable<Reservation> query, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        return query.Where(x => x.Status == ReservationStatus.Held && x.ExpiresAtUtc <= now)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.Status, ReservationStatus.Expired), ct);
    }
}
