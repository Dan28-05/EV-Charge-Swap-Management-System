using EVChargeSwap.Data;
using EVChargeSwap.Features.Reservations.Entities;
using Microsoft.EntityFrameworkCore;

namespace EVChargeSwap.Features.Reservations.Services;

internal static class ReservationLocks
{
    public static async Task<bool> LockOperatorStationAsync(
        AppDbContext db, Guid locationId, Guid operatorId, CancellationToken ct)
    {
        var location = await db.Locations.FromSqlInterpolated($"SELECT * FROM locations WHERE id = {locationId} FOR UPDATE")
            .AsNoTracking().SingleOrDefaultAsync(ct);
        if (location is null) return false;

        var assignment = await db.Set<OperatorAssignment>()
            .FromSqlInterpolated($"""
                SELECT * FROM operator_assignments
                WHERE "OperatorId" = {operatorId} AND "LocationId" = {locationId} AND "EndedAtUtc" IS NULL
                FOR UPDATE
                """)
            .AsNoTracking().SingleOrDefaultAsync(ct);
        return assignment is not null;
    }
}
