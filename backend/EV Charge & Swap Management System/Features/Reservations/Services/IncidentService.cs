using EVChargeSwap.Common.Messaging;
using EVChargeSwap.Common.Messaging.Events;
using EVChargeSwap.Data;
using EVChargeSwap.Data.Entities;
using EVChargeSwap.Features.Reservations.Entities;
using EVChargeSwap.Features.Reservations.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EVChargeSwap.Features.Reservations.Services;

public sealed record IncidentCandidate(Guid Id, string Code);
public sealed record IncidentOptions(bool CanChangeReservation, IReadOnlyList<IncidentCandidate> Candidates);

public sealed class IncidentService(AppDbContext db, TimeProvider clock, IOutboxService outbox, ReservationService reservations)
{
    public const string FaultCancellationReason = "RESOURCE_FAULT";

    internal static async Task RecordFaultAsync(AppDbContext db, IOutboxService? outbox,
        Guid locationId, Guid? bayId, Guid? batteryId, Guid reporterId, DateTimeOffset now,
        string description, CancellationToken ct)
    {
        // Push active predicate directly into the FOR UPDATE query so PostgreSQL only locks the active row,
        // never historical Completed/Cancelled/Expired rows for this bay or battery.
        var reservation = await db.Reservations
            .FromSqlInterpolated($"""
                SELECT * FROM reservations
                WHERE "LocationId" = {locationId}
                  AND "ChargingBayId" IS NOT DISTINCT FROM {bayId}
                  AND "BatteryId" IS NOT DISTINCT FROM {batteryId}
                  AND ("Status" = 'CheckedIn' OR ("Status" = 'Held' AND "ExpiresAtUtc" > {now}))
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(ct);
        var reservationId = reservation?.Id;
        var sessionId = bayId.HasValue && reservationId.HasValue
            ? await db.ChargingSessions.Where(x => x.ReservationId == reservationId)
                .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct)
            : null;
        var report = new IncidentReport
        {
            LocationId = locationId, ChargingBayId = bayId, BatteryId = batteryId,
            ReservationId = reservationId, ChargingSessionId = sessionId,
            ReportedById = reporterId, ReportedAtUtc = now, Description = description
        };
        db.IncidentReports.Add(report);
        db.AuditLogs.Add(new AuditLog
        {
            Action = "IncidentReported", ResourceType = nameof(IncidentReport),
            ResourceId = report.Id.ToString(), ActorId = reporterId, TimestampUtc = now
        });
        if (reservation is not null && sessionId is null && reservation.VehicleArrivedAtUtc is null &&
            (bayId == null || await db.ChargingBays.AnyAsync(x => x.Id == bayId &&
                x.ParkingState == ParkingState.Vacant, ct)))
        {
            var candidate = await Candidates(db, report, now).FirstOrDefaultAsync(ct);
            await ApplyResolutionAsync(db, outbox, report, reservation,
                candidate is null ? IncidentResolution.Cancelled : IncidentResolution.Reassigned,
                candidate?.Id, reporterId, now, ct);
        }
        if (outbox is not null)
            await outbox.EnqueueAsync("IncidentReported", EventRoutingKeys.IncidentReported,
                new IncidentReportedEvent(report.Id), ct);
    }

    private static async Task ApplyResolutionAsync(AppDbContext db, IOutboxService? outbox,
        IncidentReport report, Reservation reservation, IncidentResolution resolution,
        Guid? replacementId, Guid operatorId, DateTimeOffset now, CancellationToken ct)
    {
        if (resolution == IncidentResolution.Reassigned)
        {
            if (report.ChargingBayId.HasValue)
                db.Entry(reservation).Property(x => x.ChargingBayId).CurrentValue = replacementId;
            else
                db.Entry(reservation).Property(x => x.BatteryId).CurrentValue = replacementId;
            await ReservationTransitions.RecordDetailsChangeAsync(outbox, reservation, ct);
        }
        else
        {
            var entry = db.Entry(reservation);
            entry.Property(x => x.Status).CurrentValue = ReservationStatus.Cancelled;
            entry.Property(x => x.ClosedAtUtc).CurrentValue = now;
            entry.Property(x => x.ClosedById).CurrentValue = operatorId;
            entry.Property(x => x.ClosingReason).CurrentValue = FaultCancellationReason;
            await ReservationTransitions.RecordAsync(db, outbox, reservation, ReservationStatus.Cancelled,
                operatorId, now, ct, FaultCancellationReason);
            if (outbox is not null)
                await outbox.EnqueueAsync("IncidentReservationResolved", EventRoutingKeys.IncidentReservationResolved,
                    new IncidentReservationResolvedEvent(reservation.CustomerId, null), ct);
        }
        RecordResolution(db, report, resolution, operatorId, now, replacementId);
    }

    private static void RecordResolution(AppDbContext db, IncidentReport report, IncidentResolution resolution,
        Guid operatorId, DateTimeOffset now, Guid? replacementId)
    {
        report.Resolve(resolution, operatorId, now, replacementId);
        db.AuditLogs.Add(new AuditLog
        {
            Action = "IncidentResolved", ResourceType = nameof(IncidentReport), ResourceId = report.Id.ToString(),
            ActorId = operatorId, Details = resolution.ToString(), TimestampUtc = now
        });
    }

    public IQueryable<IncidentReport> ForOperator(Guid operatorId) => db.IncidentReports.AsNoTracking()
        .Where(x => db.Set<OperatorAssignment>().Any(a => a.OperatorId == operatorId &&
            a.LocationId == x.LocationId && a.EndedAtUtc == null));

    public async Task<IncidentOptions?> GetOptionsAsync(Guid id, Guid operatorId, CancellationToken ct)
    {
        var report = await ForOperator(operatorId).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (report is null) return null;
        var reservation = report.ReservationId is { } reservationId
            ? await db.Reservations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == reservationId, ct)
            : null;
        var now = clock.GetUtcNow();
        if (report.Resolution.HasValue || !await CanChangeReservationAsync(report, reservation, now, ct))
            return new IncidentOptions(false, []);
        return new IncidentOptions(true, await Candidates(db, report, now).Take(50).ToListAsync(ct));
    }

    public async Task<(Guid? LocationId, string? Conflict)> ResolveAsync(Guid id, Guid operatorId,
        IncidentResolution resolution, Guid? replacementId, CancellationToken ct)
    {
        var locationId = await ForOperator(operatorId).Where(x => x.Id == id)
            .Select(x => (Guid?)x.LocationId).SingleOrDefaultAsync(ct);
        if (locationId is not { } station) return (null, null);
        if (resolution is IncidentResolution.Reassigned or IncidentResolution.Cancelled)
            await reservations.ExpireAtStationAsync(station, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await ReservationLocks.LockOperatorStationAsync(db, station, operatorId, ct)) return (null, null);

        var report = await db.IncidentReports.SingleAsync(x => x.Id == id, ct);
        if (report.Resolution.HasValue)
            return report.Resolution == resolution &&
                (report.ReplacementChargingBayId ?? report.ReplacementBatteryId) == replacementId
                ? (station, null) : (null, "This incident has already been resolved.");

        // Cancellation and expiry do not lock the station. Lock the affected reservation as well.
        var reservation = report.ReservationId is { } reservationId
            ? await db.Reservations.FromSqlInterpolated($"""SELECT * FROM reservations WHERE "Id" = {reservationId} FOR UPDATE""")
                .SingleOrDefaultAsync(ct)
            : null;
        var now = clock.GetUtcNow();
        var canChange = await CanChangeReservationAsync(report, reservation, now, ct);
        if (resolution == IncidentResolution.Recorded)
        {
            if (canChange) return (null, "Resolve the affected reservation before closing this report.");
        }
        else
        {
            if (!canChange || reservation is null)
                return (null, "This reservation can no longer be changed through the incident workflow.");

            if (resolution == IncidentResolution.Reassigned &&
                (replacementId is null || !await Candidates(db, report, now, replacementId).AnyAsync(ct)))
                return (null, "The selected resource is no longer available.");
            if (resolution == IncidentResolution.Cancelled && await Candidates(db, report, now).AnyAsync(ct))
                return (null, "A replacement is available. Refresh the options and confirm reassignment.");

            await ApplyResolutionAsync(db, outbox, report, reservation, resolution, replacementId, operatorId, now, ct);
        }

        if (resolution == IncidentResolution.Recorded)
            RecordResolution(db, report, resolution, operatorId, now, replacementId);

        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return (station, null);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: ReservationConfiguration.BayHoldIndex or ReservationConfiguration.BatteryHoldIndex
        })
        {
            return (null, "The selected resource is no longer available.");
        }
    }

    private async Task<bool> CanChangeReservationAsync(IncidentReport report, Reservation? reservation,
        DateTimeOffset now, CancellationToken ct)
    {
        if (reservation is null || reservation.LocationId != report.LocationId ||
            reservation.ChargingBayId != report.ChargingBayId || reservation.BatteryId != report.BatteryId ||
            (reservation.Status != ReservationStatus.CheckedIn &&
                (reservation.Status != ReservationStatus.Held || reservation.ExpiresAtUtc <= now)))
            return false;

        if (await db.IncidentReports.AnyAsync(x => x.LocationId == report.LocationId &&
            x.ChargingBayId == report.ChargingBayId && x.BatteryId == report.BatteryId &&
            x.ReportedAtUtc > report.ReportedAtUtc, ct))
            return false;

        if (report.ChargingBayId is { } bayId)
            return reservation.VehicleArrivedAtUtc is null &&
                !await db.ChargingSessions.AnyAsync(x => x.ReservationId == reservation.Id, ct) &&
                await db.ChargingBays.AnyAsync(x => x.Id == bayId && x.ParkingState == ParkingState.Vacant &&
                    x.EquipmentState == ChargerState.Faulted, ct);
        return await db.Batteries.AnyAsync(x => x.Id == report.BatteryId && x.LocationId == report.LocationId &&
            x.State == BatteryState.Faulted, ct);
    }

    private static IQueryable<IncidentCandidate> Candidates(AppDbContext db, IncidentReport report, DateTimeOffset now, Guid? id = null)
    {
        // Match PostgreSQL partial unique indexes (ux_reservations_active_bay / ux_reservations_active_battery)
        // which block all Held and CheckedIn rows until expired rows are transitioned to Expired.
        var active = db.Reservations.Where(x =>
            x.Status == ReservationStatus.CheckedIn || x.Status == ReservationStatus.Held);
        if (report.ChargingBayId.HasValue)
            return db.ChargingBays.Where(x => (id == null || x.Id == id) &&
                    x.LocationId == report.LocationId && x.ParkingState == ParkingState.Vacant &&
                    x.EquipmentState == ChargerState.Active && !active.Any(r => r.ChargingBayId == x.Id))
                .OrderBy(x => x.Code).Select(x => new IncidentCandidate(x.Id, x.Code));
        return db.Batteries.Where(x => (id == null || x.Id == id) &&
                x.LocationId == report.LocationId && x.State == BatteryState.Ready &&
                !active.Any(r => r.BatteryId == x.Id))
            .OrderBy(x => x.Serial).Select(x => new IncidentCandidate(x.Id, x.Serial));
    }
}
