using EVChargeSwap.Data;
using EVChargeSwap.Features.Authorization;
using EVChargeSwap.Features.Reservations.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EVChargeSwap.Features.Reservations.Services;

public sealed class OperatorAssignmentService(AppDbContext db, TimeProvider clock)
{
    public async Task<(OperatorAssignment? Assignment, string? Error)> AssignAsync(
        Guid operatorId, Guid locationId, Guid adminId, bool transfer, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var user = await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE id = {operatorId} FOR UPDATE")
            .AsNoTracking().SingleOrDefaultAsync(ct);
        if (user is null || !user.IsActive ||
            !await db.UserRoles.AnyAsync(x => x.UserId == operatorId && x.RoleId == AppRoles.OperatorId, ct))
            return (null, "The Operator account is missing or inactive.");

        if (!await db.Locations.AnyAsync(x => x.Id == locationId, ct))
            return (null, "The location does not exist.");

        var current = await db.Set<OperatorAssignment>()
            .SingleOrDefaultAsync(x => x.OperatorId == operatorId && x.EndedAtUtc == null, ct);
        if (current is not null && !transfer)
            return (null, "The Operator already has an active assignment.");
        if (current?.LocationId == locationId)
            return (null, "The Operator is already assigned to this location.");

        var now = clock.GetUtcNow();
        if (current is not null) current.EndedAtUtc = now;
        var assignment = new OperatorAssignment
        {
            OperatorId = operatorId,
            LocationId = locationId,
            AssignedByAdminId = adminId,
            AssignedAtUtc = now
        };
        db.Add(assignment);
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return (assignment, null);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ux_operator_active_assignment" })
        {
            return (null, "The Operator already has an active assignment.");
        }
    }

    public Task<int> EndAsync(Guid operatorId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        return db.Set<OperatorAssignment>()
            .Where(x => x.OperatorId == operatorId && x.EndedAtUtc == null)
            .ExecuteUpdateAsync(x => x.SetProperty(a => a.EndedAtUtc, now), ct);
    }
}
