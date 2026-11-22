using System.Security.Cryptography;
using EVChargeSwap.Common.Messaging;
using EVChargeSwap.Data;
using EVChargeSwap.Features.Locations.Entities;
using EVChargeSwap.Features.Reservations.Entities;
using EVChargeSwap.Features.Reservations.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EVChargeSwap.Features.Reservations.Services;

public sealed class ReservationService(AppDbContext db, TimeProvider clock, IOutboxService? outbox = null)
{
    private const int ExpiryBatchSize = 100;
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public IQueryable<Reservation> Active => db.Set<Reservation>()
        .Where(x => x.Status == ReservationStatus.Held || x.Status == ReservationStatus.CheckedIn);

    public Task<int> ExpireAsync(CancellationToken ct) => ExpireAsync(db.Set<Reservation>(), ct);

    internal Task<int> ExpireAtStationAsync(Guid locationId, CancellationToken ct) =>
        ExpireAsync(db.Reservations.Where(x => x.LocationId == locationId), ct);

    public async Task<(Reservation? Reservation, string? Conflict, bool Replayed)> HoldAsync(
        Guid customerId, Guid locationId, ReservationServiceType serviceType, CancellationToken ct,
        Guid? idempotencyKey = null)
    {
        if (!Enum.IsDefined(serviceType) || idempotencyKey == Guid.Empty)
            throw new ArgumentException("Invalid reservation service or idempotency key.");

        // Process elapsed holds even when the periodic job is delayed.
        await ExpireAsync(db.Set<Reservation>().Where(x => x.CustomerId == customerId || x.LocationId == locationId), ct);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (idempotencyKey is { } key)
        {
            // Serialize retries for this key even if the requests name different stations.
            var lockKey = $"reservation:{customerId:N}:{key:N}";
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", ct);
            var existing = await db.Reservations.AsNoTracking()
                .SingleOrDefaultAsync(x => x.CustomerId == customerId && x.IdempotencyKey == key, ct);
            if (existing is not null)
            {
                var existingService = existing.ChargingBayId.HasValue
                    ? ReservationServiceType.Charging : ReservationServiceType.BatterySwap;
                return existing.LocationId == locationId && existingService == serviceType
                    ? (existing, null, true)
                    : (null, "Idempotency-Key was already used for a different reservation request.", false);
            }
        }

        var location = await db.Locations
            .FromSqlInterpolated($"SELECT * FROM locations WHERE id = {locationId} FOR UPDATE")
            .AsNoTracking().SingleOrDefaultAsync(ct);

        if (location is null || !location.IsActive)
            return (null, null, false);

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
            return (null, "The requested service is not open for booking.", false);

        if (await Active.AnyAsync(x => x.CustomerId == customerId, ct))
            return (null, "You already have an active reservation.", false);

        // Completion may release an active visit and create its invoice in a single transaction at another station.
        if (await db.Invoices.AnyAsync(x => x.CustomerId == customerId && x.Status == InvoiceStatus.Pending, ct))
            return (null, "Bạn có hóa đơn chưa thanh toán. Vui lòng thanh toán trong Lịch sử hóa đơn trước khi đặt chỗ mới.", false);

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
            return (null, "No resource is available for the requested service.", false);

        var reservation = new Reservation
        {
            CustomerId = customerId,
            IdempotencyKey = idempotencyKey,
            LocationId = locationId,
            ChargingBayId = bayId,
            BatteryId = batteryId,
            Code = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(15)
        };

        db.Add(reservation);
        await ReservationTransitions.RecordAsync(db, outbox, reservation, ReservationStatus.Held, customerId, now, ct);

        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return (reservation, null, false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: ReservationConfiguration.CustomerHoldIndex or
                ReservationConfiguration.BayHoldIndex or ReservationConfiguration.BatteryHoldIndex
        })
        {
            return (null, "An active reservation already holds the customer or resource.", false);
        }
    }

    public async Task<int> CancelAsync(Guid customerId, Guid id, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = clock.GetUtcNow();
        var updated = await db.Reservations.Where(x => x.Id == id && x.CustomerId == customerId &&
                x.Status == ReservationStatus.Held && x.ExpiresAtUtc > now)
            .ExecuteUpdateAsync(update => update
                .SetProperty(x => x.Status, ReservationStatus.Cancelled)
                .SetProperty(x => x.ClosedAtUtc, now)
                .SetProperty(x => x.ClosedById, customerId), ct);
        if (updated == 0) return 0;
        await RecordTransitionAsync(id, ReservationStatus.Cancelled, customerId, now, ct);
        await tx.CommitAsync(ct);
        return updated;
    }

    public async Task<int> CheckInAsync(Guid locationId, Guid operatorId, string code, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await ReservationLocks.LockOperatorStationAsync(db, locationId, operatorId, ct)) return 0;
        var now = clock.GetUtcNow();
        var reservationId = await db.Reservations.Where(x => x.LocationId == locationId && x.Code == code)
            .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
        if (reservationId is not { } id) return 0;
        var updated = await db.Reservations.Where(x => x.Id == id &&
                x.Status == ReservationStatus.Held && x.ExpiresAtUtc > now &&
                (x.ChargingBayId != null && db.Set<ChargingBay>().Any(b => b.Id == x.ChargingBayId &&
                    b.EquipmentState == ChargerState.Active && b.ParkingState == ParkingState.Vacant) ||
                 x.BatteryId != null && db.Set<Battery>().Any(b => b.Id == x.BatteryId && b.State == BatteryState.Ready)))
            .ExecuteUpdateAsync(update => update
                .SetProperty(x => x.Status, ReservationStatus.CheckedIn)
                .SetProperty(x => x.CheckedInAtUtc, now)
                .SetProperty(x => x.CheckedInById, operatorId), ct);
        if (updated == 0) return 0;
        await RecordTransitionAsync(id, ReservationStatus.CheckedIn, operatorId, now, ct);
        await tx.CommitAsync(ct);
        return updated;
    }

    // Called only after the operator confirms there was no service and the bay is clear.
    public async Task<int> CloseWithoutServiceAsync(Guid locationId, Guid operatorId, Guid reservationId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await ReservationLocks.LockOperatorStationAsync(db, locationId, operatorId, ct)) return 0;
        var now = clock.GetUtcNow();
        var updated = await db.Reservations.Where(x => x.Id == reservationId && x.LocationId == locationId &&
                x.Status == ReservationStatus.CheckedIn &&
                (x.ChargingBayId == null || x.VehicleArrivedAtUtc == null &&
                    !db.Set<ChargingSession>().Any(s => s.ReservationId == x.Id) &&
                    db.Set<ChargingBay>().Any(b => b.Id == x.ChargingBayId &&
                    b.ParkingState == ParkingState.Vacant)))
            .ExecuteUpdateAsync(update => update
                .SetProperty(x => x.Status, ReservationStatus.Completed)
                .SetProperty(x => x.ClosingReason, x => x.ChargingBayId != null ? "DID_NOT_CHARGE" : "DID_NOT_SWAP")
                .SetProperty(x => x.ClosedById, operatorId)
                .SetProperty(x => x.ClosedAtUtc, now), ct);
        if (updated == 0) return 0;
        await RecordTransitionAsync(reservationId, ReservationStatus.Completed, operatorId, now, ct);
        await tx.CommitAsync(ct);
        return updated;
    }

    private async Task<int> ExpireAsync(IQueryable<Reservation> query, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var due = query.Where(x => x.Status == ReservationStatus.Held && x.ExpiresAtUtc <= now);
        // Fast-path: avoid opening/rolling back a transaction on every HoldAsync when nothing is expired.
        if (!await due.AsNoTracking().AnyAsync(ct)) return 0;

        var total = 0;
        while (true)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var batch = await due.AsNoTracking().OrderBy(x => x.Id).Take(ExpiryBatchSize).ToListAsync(ct);
            if (batch.Count == 0) return total;

            var batchIds = batch.Select(x => x.Id).ToArray();
            var updatedCount = await due.Where(x => batchIds.Contains(x.Id)).ExecuteUpdateAsync(update => update
                .SetProperty(x => x.Status, ReservationStatus.Expired)
                .SetProperty(x => x.ClosedAtUtc, now), ct);

            if (updatedCount > 0)
            {
                // Fetch only the rows actually transitioned in this transaction when partial concurrency occurred.
                var expiredBatch = updatedCount == batch.Count
                    ? batch
                    : await db.Reservations.AsNoTracking()
                        .Where(x => batchIds.Contains(x.Id) && x.Status == ReservationStatus.Expired && x.ClosedAtUtc == now)
                        .ToListAsync(ct);

                foreach (var reservation in expiredBatch)
                {
                    await ReservationTransitions.RecordAsync(db, outbox, reservation, ReservationStatus.Expired, null, now, ct);
                    total++;
                }

                await db.SaveChangesAsync(ct);
            }

            await tx.CommitAsync(ct);
            if (batch.Count < ExpiryBatchSize) return total;
        }
    }

    private async Task RecordTransitionAsync(Guid id, ReservationStatus status, Guid actorId,
        DateTimeOffset now, CancellationToken ct)
    {
        var reservation = await db.Reservations.AsNoTracking().SingleAsync(x => x.Id == id, ct);
        await ReservationTransitions.RecordAsync(db, outbox, reservation, status, actorId, now, ct, reservation.ClosingReason);
        await db.SaveChangesAsync(ct);
    }
}
