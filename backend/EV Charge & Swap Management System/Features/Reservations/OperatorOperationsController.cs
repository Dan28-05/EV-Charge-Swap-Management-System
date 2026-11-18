using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using EVChargeSwap.Data;
using EVChargeSwap.Features.Authorization;
using EVChargeSwap.Features.Locations.Caching;
using EVChargeSwap.Features.Locations.Services;
using EVChargeSwap.Features.Reservations.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EVChargeSwap.Features.Reservations;

public sealed record ChangeBayStateRequest([EnumDataType(typeof(ChargerState))] ChargerState State);
public sealed record ChangeBatteryStateRequest([EnumDataType(typeof(BatteryState))] BatteryState State);
public sealed record ServiceIntervalRequest([Range(0, 1439)] int OpensAtMinute,
    [Range(1, 1440)] int ClosesAtMinute);
public sealed record ReplaceHoursRequest([EnumDataType(typeof(ReservationServiceType))] ReservationServiceType ServiceType,
    [EnumDataType(typeof(DayOfWeek))] DayOfWeek Day, IReadOnlyList<ServiceIntervalRequest> Intervals);

[ApiController]
[Authorize(Roles = AppRoles.Operator)]
[Route("api/operator/me")]
public sealed class OperatorOperationsController(AppDbContext db, TimeProvider clock, ILocationCache cache, IStationNotifier? notifier = null) : ControllerBase
{
    [HttpGet("station")]
    public async Task<IActionResult> Station(CancellationToken ct)
    {
        var locationId = await AssignedLocationAsync(ct);
        if (locationId is null) return NotFound();
        var location = await db.Locations.AsNoTracking().Where(x => x.Id == locationId).Select(x => new
        {
            x.Id, x.Name, x.Address, x.OperatingStatus, x.OffersCarCharging, x.OffersBatterySwap
        }).SingleAsync(ct);
        var bays = await db.Set<ChargingBay>().AsNoTracking().Where(x => x.LocationId == locationId)
            .OrderBy(x => x.Code).ToListAsync(ct);
        var batteries = await db.Set<Battery>().AsNoTracking().Where(x => x.LocationId == locationId)
            .OrderBy(x => x.Serial).ToListAsync(ct);
        var hours = await db.Set<ServiceHours>().AsNoTracking().Where(x => x.LocationId == locationId)
            .OrderBy(x => x.ServiceType).ThenBy(x => x.Day).ThenBy(x => x.OpensAtMinute).ToListAsync(ct);
        var reservations = await db.Set<Reservation>().AsNoTracking()
            .Where(x => x.LocationId == locationId &&
                (x.Status == ReservationStatus.CheckedIn || x.Status == ReservationStatus.Held && x.ExpiresAtUtc > clock.GetUtcNow()))
            .OrderBy(x => x.ExpiresAtUtc)
            .Select(x => new { x.Id, x.ChargingBayId, x.BatteryId, x.Status, x.ExpiresAtUtc, x.CheckedInAtUtc })
            .ToListAsync(ct);
        return Ok(new { location, bays, batteries, hours, reservations });
    }

    [HttpPatch("charging-bays/{id:guid}/equipment-state")]
    public async Task<IActionResult> ChangeBay(Guid id, ChangeBayStateRequest request, CancellationToken ct)
    {
        if (!Enum.IsDefined(request.State)) return BadRequest();
        var locationId = await AssignedLocationAsync(ct);
        if (locationId is null) return NotFound();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockLocationAsync(locationId.Value, ct);
        if (!await StillAssignedAsync(locationId.Value, ct)) return Forbid();
        var bay = await db.Set<ChargingBay>().SingleOrDefaultAsync(x => x.Id == id && x.LocationId == locationId, ct);
        if (bay is null) return NotFound();
        db.Entry(bay).Property(x => x.EquipmentState).CurrentValue = request.State;
        bay.StateUpdatedById = UserId();
        bay.StateUpdatedAtUtc = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await cache.InvalidateLocationAsync(locationId.Value, ct);
        if (notifier is not null) await notifier.NotifyStationInventoryChangedAsync(locationId.Value, ct);
        return NoContent();
    }

    [HttpPatch("batteries/{id:guid}/state")]
    public async Task<IActionResult> ChangeBattery(Guid id, ChangeBatteryStateRequest request, CancellationToken ct)
    {
        if (!Enum.IsDefined(request.State)) return BadRequest();
        var locationId = await AssignedLocationAsync(ct);
        if (locationId is null) return NotFound();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockLocationAsync(locationId.Value, ct);
        if (!await StillAssignedAsync(locationId.Value, ct)) return Forbid();
        var battery = await db.Set<Battery>().SingleOrDefaultAsync(x => x.Id == id && x.LocationId == locationId, ct);
        if (battery is null) return NotFound();
        var next = request.State;
        var allowed = (battery.State, next) switch
        {
            (BatteryState.Ready, BatteryState.Faulted) => true,
            (BatteryState.Inspection, BatteryState.Charging or BatteryState.Faulted) => true,
            (BatteryState.Charging, BatteryState.Ready or BatteryState.Faulted) => true,
            (BatteryState.Faulted, BatteryState.Inspection) => true,
            _ => false
        };
        if (!allowed) return Conflict("This battery transition requires a service or inspection workflow.");
        if (next != BatteryState.Faulted && await db.Set<Reservation>().AnyAsync(x => x.BatteryId == id &&
            (x.Status == ReservationStatus.Held || x.Status == ReservationStatus.CheckedIn), ct))
            return Conflict("Resolve the existing reservation before changing this battery.");
        db.Entry(battery).Property(x => x.State).CurrentValue = next;
        battery.StateUpdatedById = UserId();
        battery.StateUpdatedAtUtc = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await cache.InvalidateLocationAsync(locationId.Value, ct);
        if (notifier is not null) await notifier.NotifyStationInventoryChangedAsync(locationId.Value, ct);
        return NoContent();
    }

    [HttpPut("service-hours")]
    public async Task<IActionResult> ReplaceHours(ReplaceHoursRequest request, CancellationToken ct)
    {
        if (!Enum.IsDefined(request.ServiceType) || !Enum.IsDefined(request.Day) || request.Intervals is null ||
            request.Intervals.Any(x => x.OpensAtMinute < 0 || x.ClosesAtMinute > 1440 ||
                x.OpensAtMinute >= x.ClosesAtMinute)) return BadRequest();
        var ordered = request.Intervals.OrderBy(x => x.OpensAtMinute).ToArray();
        if (ordered.Skip(1).Where((x, i) => x.OpensAtMinute < ordered[i].ClosesAtMinute).Any())
            return BadRequest("Service intervals overlap.");
        var locationId = await AssignedLocationAsync(ct);
        if (locationId is null) return NotFound();
        var station = await db.Locations.AsNoTracking().SingleAsync(x => x.Id == locationId, ct);
        if (request.ServiceType == ReservationServiceType.Charging && !station.OffersCarCharging ||
            request.ServiceType == ReservationServiceType.BatterySwap && !station.OffersBatterySwap)
            return Conflict("This station does not offer the service.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockLocationAsync(locationId.Value, ct);
        if (!await StillAssignedAsync(locationId.Value, ct)) return Forbid();
        await db.Set<ServiceHours>().Where(x => x.LocationId == locationId && x.ServiceType == request.ServiceType &&
            x.Day == request.Day).ExecuteDeleteAsync(ct);
        db.AddRange(ordered.Select(x => new ServiceHours
        {
            LocationId = locationId.Value, ServiceType = request.ServiceType, Day = request.Day,
            OpensAtMinute = x.OpensAtMinute, ClosesAtMinute = x.ClosesAtMinute,
            UpdatedById = UserId(), UpdatedAtUtc = clock.GetUtcNow()
        }));
        await db.SaveChangesAsync(ct);
        var offeredServices = new List<ReservationServiceType>();
        if (station.OffersCarCharging) offeredServices.Add(ReservationServiceType.Charging);
        if (station.OffersBatterySwap) offeredServices.Add(ReservationServiceType.BatterySwap);
        var allHours = await db.Set<ServiceHours>().AsNoTracking()
            .Where(x => x.LocationId == locationId).ToListAsync(ct);
        var isOpen24Hours = offeredServices.All(service => Enumerable.Range(0, 7).All(day =>
            allHours.Any(x => x.ServiceType == service && (int)x.Day == day &&
                x.OpensAtMinute == 0 && x.ClosesAtMinute == 1440)));
        await db.Locations.Where(x => x.Id == locationId)
            .ExecuteUpdateAsync(x => x.SetProperty(l => l.IsOpen24Hours, isOpen24Hours), ct);
        await transaction.CommitAsync(ct);
        await cache.InvalidateLocationAsync(locationId.Value, ct);
        if (notifier is not null) await notifier.NotifyStationInventoryChangedAsync(locationId.Value, ct);
        return NoContent();
    }

    private Guid UserId() => Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

    private Task<Guid?> AssignedLocationAsync(CancellationToken ct) => db.Set<OperatorAssignment>()
        .Where(x => x.OperatorId == UserId() && x.EndedAtUtc == null)
        .Select(x => (Guid?)x.LocationId).SingleOrDefaultAsync(ct);

    private Task<bool> StillAssignedAsync(Guid locationId, CancellationToken ct) => db.Set<OperatorAssignment>()
        .AnyAsync(x => x.OperatorId == UserId() && x.LocationId == locationId && x.EndedAtUtc == null, ct);

    private async Task LockLocationAsync(Guid id, CancellationToken ct) =>
        await db.Locations.FromSqlInterpolated($"SELECT * FROM locations WHERE id = {id} FOR UPDATE")
            .AsNoTracking().Select(x => x.Id).SingleAsync(ct);
}
