using EVChargeSwap.Data;
using EVChargeSwap.Features.Reservations.Entities;
using Microsoft.EntityFrameworkCore;

namespace EVChargeSwap.Features.Reservations.Services;

// Every physical transition is initiated by an assigned Operator and serialized with booking at the station.
public sealed class ChargingService(AppDbContext db, TimeProvider clock)
{
    public async Task<bool> VehicleArrivedAsync(Guid reservationId, Guid operatorId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var reservation = await LockedReservationAsync(reservationId, operatorId, ct);
        if (reservation?.ChargingBayId is not { } bayId || reservation.Status != ReservationStatus.CheckedIn ||
            reservation.VehicleArrivedAtUtc is not null) return false;

        var bay = await db.Set<ChargingBay>().SingleAsync(x => x.Id == bayId, ct);
        if (bay.ParkingState != ParkingState.Vacant) return false;

        db.Entry(reservation).Property(x => x.VehicleArrivedAtUtc).CurrentValue = clock.GetUtcNow();
        db.Entry(reservation).Property(x => x.VehicleArrivedById).CurrentValue = operatorId;
        db.Entry(bay).Property(x => x.ParkingState).CurrentValue = ParkingState.Occupied;
        bay.StateUpdatedById = operatorId;
        bay.StateUpdatedAtUtc = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<ChargingSession?> StartAsync(Guid reservationId, Guid operatorId, decimal startMeterKwh, CancellationToken ct)
    {
        if (startMeterKwh < 0 || startMeterKwh > 999_999_999.999m ||
            decimal.Round(startMeterKwh, 3) != startMeterKwh) return null;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var reservation = await LockedReservationAsync(reservationId, operatorId, ct);
        if (reservation?.ChargingBayId is not { } bayId || reservation.Status != ReservationStatus.CheckedIn ||
            reservation.VehicleArrivedAtUtc is null || reservation.NoChargeConfirmedAtUtc is not null ||
            await db.Set<ChargingSession>().AnyAsync(x => x.ReservationId == reservationId, ct)) return null;

        var bay = await db.Set<ChargingBay>().SingleAsync(x => x.Id == bayId, ct);
        if (bay.ParkingState != ParkingState.Occupied || bay.EquipmentState != ChargerState.Active) return null;

        var session = new ChargingSession
        {
            ReservationId = reservationId, ChargingBayId = bayId, StartedById = operatorId,
            StartedAtUtc = clock.GetUtcNow(), StartMeterKwh = startMeterKwh
        };
        db.Add(session);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return session;
    }

    public async Task<bool> CompleteAsync(Guid reservationId, Guid operatorId, decimal endMeterKwh, CancellationToken ct)
    {
        if (endMeterKwh < 0 || endMeterKwh > 999_999_999.999m ||
            decimal.Round(endMeterKwh, 3) != endMeterKwh) return false;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var reservation = await LockedReservationAsync(reservationId, operatorId, ct);
        if (reservation?.ChargingBayId is null || reservation.Status != ReservationStatus.CheckedIn ||
            reservation.VehicleDepartedAtUtc is not null) return false;

        var session = await db.Set<ChargingSession>().SingleOrDefaultAsync(x => x.ReservationId == reservationId, ct);
        if (session is null || session.CompletedAtUtc is not null || endMeterKwh < session.StartMeterKwh) return false;

        db.Entry(session).Property(x => x.CompletedAtUtc).CurrentValue = clock.GetUtcNow();
        db.Entry(session).Property(x => x.CompletedById).CurrentValue = operatorId;
        db.Entry(session).Property(x => x.EndMeterKwh).CurrentValue = endMeterKwh;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<bool> ConfirmNoChargeAsync(Guid reservationId, Guid operatorId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var reservation = await LockedReservationAsync(reservationId, operatorId, ct);
        if (reservation?.ChargingBayId is not { } bayId || reservation.Status != ReservationStatus.CheckedIn ||
            reservation.VehicleArrivedAtUtc is null || reservation.NoChargeConfirmedAtUtc is not null ||
            await db.Set<ChargingSession>().AnyAsync(x => x.ReservationId == reservationId, ct)) return false;
        if (!await db.Set<ChargingBay>().AnyAsync(x => x.Id == bayId && x.ParkingState == ParkingState.Occupied, ct))
            return false;

        db.Entry(reservation).Property(x => x.NoChargeConfirmedAtUtc).CurrentValue = clock.GetUtcNow();
        db.Entry(reservation).Property(x => x.NoChargeConfirmedById).CurrentValue = operatorId;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<bool> VehicleDepartedAsync(Guid reservationId, Guid operatorId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var reservation = await LockedReservationAsync(reservationId, operatorId, ct);
        if (reservation?.ChargingBayId is not { } bayId || reservation.Status != ReservationStatus.CheckedIn ||
            reservation.VehicleArrivedAtUtc is null || reservation.VehicleDepartedAtUtc is not null) return false;

        var session = await db.Set<ChargingSession>().SingleOrDefaultAsync(x => x.ReservationId == reservationId, ct);
        if (session is null && reservation.NoChargeConfirmedAtUtc is null ||
            session is not null && session.CompletedAtUtc is null) return false;

        var bay = await db.Set<ChargingBay>().SingleAsync(x => x.Id == bayId, ct);
        if (bay.ParkingState != ParkingState.Occupied) return false;

        var now = clock.GetUtcNow();
        db.Entry(reservation).Property(x => x.VehicleDepartedAtUtc).CurrentValue = now;
        db.Entry(reservation).Property(x => x.VehicleDepartedById).CurrentValue = operatorId;
        db.Entry(reservation).Property(x => x.Status).CurrentValue = ReservationStatus.Completed;
        db.Entry(reservation).Property(x => x.ClosingReason).CurrentValue = session is null ? "DID_NOT_CHARGE" : null;
        db.Entry(reservation).Property(x => x.ClosedById).CurrentValue = operatorId;
        db.Entry(reservation).Property(x => x.ClosedAtUtc).CurrentValue = now;
        db.Entry(bay).Property(x => x.ParkingState).CurrentValue = ParkingState.Vacant;
        bay.StateUpdatedById = operatorId;
        bay.StateUpdatedAtUtc = now;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    private async Task<Reservation?> LockedReservationAsync(Guid id, Guid operatorId, CancellationToken ct)
    {
        var locationId = await db.Set<Reservation>().AsNoTracking().Where(x => x.Id == id)
            .Select(x => (Guid?)x.LocationId).SingleOrDefaultAsync(ct);
        if (locationId is null) return null;
        await db.Locations.FromSqlInterpolated($"SELECT * FROM locations WHERE id = {locationId.Value} FOR UPDATE")
            .AsNoTracking().SingleAsync(ct);
        var assignment = await db.Set<OperatorAssignment>()
            .FromSqlInterpolated($"SELECT * FROM operator_assignments WHERE \"OperatorId\" = {operatorId} AND \"LocationId\" = {locationId.Value} AND \"EndedAtUtc\" IS NULL FOR UPDATE")
            .AsNoTracking().SingleOrDefaultAsync(ct);
        if (assignment is null) return null;
        return await db.Set<Reservation>().SingleAsync(x => x.Id == id, ct);
    }
}
