using System.ComponentModel.DataAnnotations;
using EVChargeSwap.Data;
using EVChargeSwap.Features.Authorization;
using EVChargeSwap.Features.Locations.Caching;
using EVChargeSwap.Features.Locations.Entities;
using EVChargeSwap.Features.Locations.Responses;
using EVChargeSwap.Features.Locations.Services;
using EVChargeSwap.Features.Reservations.Entities;
using EVChargeSwap.Features.Users.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EVChargeSwap.Features.Admin;

public sealed record AdminBayItem(Guid Id, string Code, int PowerKw, string EquipmentState, string ParkingState);
public sealed record AdminBatteryItem(Guid Id, string Serial, string State);
public sealed record AdminServiceHoursItem(Guid Id, string ServiceType, int Day, int OpensAtMinute, int ClosesAtMinute);
public sealed record AdminAssignedOperatorItem(Guid OperatorId, string FullName, string Email, string? PhoneNumber, DateTimeOffset? AssignedAtUtc);

public sealed record StationReadinessChecklist(
    bool HasValidCoordinates,
    bool HasRequiredResources,
    bool HasServiceHours,
    bool HasAssignedOperator,
    bool CanGoLive);

public sealed record AdminLocationResourcesResponse(
    AdminLocationResponse Station,
    IReadOnlyList<AdminBayItem> Bays,
    IReadOnlyList<AdminBatteryItem> Batteries,
    IReadOnlyList<AdminServiceHoursItem> ServiceHours,
    IReadOnlyList<AdminAssignedOperatorItem> AssignedOperators,
    StationReadinessChecklist Readiness);

public sealed class BatchCreateBaysRequest
{
    [Required, StringLength(32, MinimumLength = 1)]
    public string Prefix { get; init; } = "BAY-";

    [Range(1, 20)]
    public int Count { get; init; } = 1;

    [Range(1, 999)]
    public int StartNumber { get; init; } = 1;

    [Range(1, 1000)]
    public int PowerKw { get; init; } = 150;
}

public sealed class BatchCreateBatteriesRequest
{
    [Required, StringLength(40, MinimumLength = 1)]
    public string Prefix { get; init; } = "BAT-";

    [Range(1, 50)]
    public int Count { get; init; } = 1;

    [Range(1, 999)]
    public int StartNumber { get; init; } = 1;
}

public sealed class ApplyServiceHoursPresetRequest
{
    [Range(0, 1439)]
    public int OpensAtMinute { get; init; } = 0;

    [Range(1, 1440)]
    public int ClosesAtMinute { get; init; } = 1440;
}

public sealed record BatchProvisionResult(int CreatedCount, int SkippedCount, IReadOnlyList<string> CreatedCodes);

public sealed record AdminOperatorSummary(
    Guid Id,
    string FullName,
    string Email,
    string? PhoneNumber,
    bool IsActive,
    Guid? AssignedLocationId,
    string? AssignedLocationName,
    DateTimeOffset? AssignedAtUtc);

public sealed record AdminLocationReportItem(
    Guid LocationId,
    string LocationName,
    bool IsActive,
    string OperatingStatus,
    int ChargingSessionCount,
    decimal TotalKwh,
    int BatterySwapCount,
    decimal PaidRevenue,
    decimal PendingAmount,
    int TotalBays,
    int OccupiedBays,
    int FaultedBays,
    int TotalBatteries,
    int ReadyBatteries,
    int IncidentCount,
    int OpenIncidentCount);

public sealed record AdminReportsSummaryResponse(
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    int TotalStations,
    int ActiveStations,
    int ChargingSessionCount,
    decimal TotalKwh,
    int BatterySwapCount,
    decimal PaidRevenue,
    decimal PendingAmount,
    int OverdueInvoiceCount,
    int TotalBays,
    int OccupiedBays,
    int FaultedBays,
    decimal BayUtilizationPercent,
    int IncidentCount,
    int OpenIncidentCount,
    IReadOnlyList<AdminLocationReportItem> ByLocation);

[ApiController]
[Authorize(Roles = AppRoles.Admin)]
[Route("api/admin")]
public sealed class AdminController(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    ILocationCache? cache = null,
    IStationNotifier? notifier = null,
    TimeProvider? clock = null) : ControllerBase
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    [HttpGet("locations/{locationId:guid}/resources")]
    public async Task<ActionResult<AdminLocationResourcesResponse>> GetLocationResources(Guid locationId, CancellationToken ct)
    {
        var location = await db.Locations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == locationId, ct);
        if (location is null) return NotFound();

        var bays = await db.ChargingBays.AsNoTracking()
            .Where(x => x.LocationId == locationId)
            .OrderBy(x => x.Code)
            .Select(x => new AdminBayItem(x.Id, x.Code, x.PowerKw, x.EquipmentState.ToString(), x.ParkingState.ToString()))
            .ToListAsync(ct);

        var batteries = await db.Batteries.AsNoTracking()
            .Where(x => x.LocationId == locationId)
            .OrderBy(x => x.Serial)
            .Select(x => new AdminBatteryItem(x.Id, x.Serial, x.State.ToString()))
            .ToListAsync(ct);

        var serviceHours = await db.Set<ServiceHours>().AsNoTracking()
            .Where(x => x.LocationId == locationId)
            .OrderBy(x => x.ServiceType).ThenBy(x => x.Day).ThenBy(x => x.OpensAtMinute)
            .Select(x => new AdminServiceHoursItem(x.Id, x.ServiceType.ToString(), (int)x.Day, x.OpensAtMinute, x.ClosesAtMinute))
            .ToListAsync(ct);

        var assignedOperators = (await (
            from assignment in db.Set<OperatorAssignment>().AsNoTracking()
            join user in db.Users.AsNoTracking() on assignment.OperatorId equals user.Id
            where assignment.LocationId == locationId && assignment.EndedAtUtc == null
            select new AdminAssignedOperatorItem(
                user.Id,
                user.FullName,
                user.Email ?? string.Empty,
                user.PhoneNumber,
                assignment.AssignedAtUtc)
        ).ToListAsync(ct))
        .OrderByDescending(x => x.AssignedAtUtc)
        .ToList();

        var hasValidCoordinates = location.Latitude is >= -90 and <= 90 && location.Longitude is >= -180 and <= 180
            && (location.Latitude != 0 || location.Longitude != 0);
        var hasRequiredResources =
            (!location.OffersCarCharging || bays.Count > 0) &&
            (!location.OffersBatterySwap || batteries.Count > 0);
        var hasServiceHours =
            (!location.OffersCarCharging || serviceHours.Any(h => h.ServiceType == nameof(ReservationServiceType.Charging))) &&
            (!location.OffersBatterySwap || serviceHours.Any(h => h.ServiceType == nameof(ReservationServiceType.BatterySwap)));
        var hasAssignedOperator = assignedOperators.Count > 0;
        var readiness = new StationReadinessChecklist(
            hasValidCoordinates,
            hasRequiredResources,
            hasServiceHours,
            hasAssignedOperator,
            hasValidCoordinates && hasRequiredResources && hasServiceHours && hasAssignedOperator);

        var locResponse = new LocationResponse(
            location.Id, location.Name, location.Address, location.Latitude, location.Longitude,
            location.OffersCarCharging, location.OffersBatterySwap, location.OperatingStatus,
            location.IsOpen24Hours, location.ChargingPowerKw, location.BatteryCapacityKwh,
            batteries.Count(x => x.State == nameof(BatteryState.Ready)),
            bays.Count(x => x.EquipmentState == nameof(ChargerState.Active) && x.ParkingState == nameof(ParkingState.Vacant)));

        return Ok(new AdminLocationResourcesResponse(
            new AdminLocationResponse(locResponse, location.IsActive),
            bays,
            batteries,
            serviceHours,
            assignedOperators,
            readiness));
    }

    [HttpPost("locations/{locationId:guid}/charging-bays/batch")]
    public async Task<ActionResult<BatchProvisionResult>> BatchCreateBays(
        Guid locationId, BatchCreateBaysRequest request, CancellationToken ct)
    {
        var location = await db.Locations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == locationId, ct);
        if (location is null) return NotFound();
        if (!location.OffersCarCharging)
            return Problem(statusCode: StatusCodes.Status409Conflict, detail: "Trạm này chưa bật dịch vụ sạc ô tô.");

        var prefix = request.Prefix.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(prefix))
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: "Tiền tố mã trụ không được để trống.");

        var existingCodes = (await db.ChargingBays.AsNoTracking()
            .Where(x => x.LocationId == locationId)
            .Select(x => x.Code)
            .ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var created = new List<string>();
        var skipped = 0;

        for (var i = 0; i < request.Count; i++)
        {
            var number = request.StartNumber + i;
            var code = $"{prefix}{number:D2}";
            if (code.Length > 64)
                return Problem(statusCode: StatusCodes.Status400BadRequest, detail: "Mã trụ sạc vượt quá 64 ký tự.");

            if (existingCodes.Contains(code))
            {
                skipped++;
                continue;
            }

            existingCodes.Add(code);
            db.ChargingBays.Add(new ChargingBay
            {
                LocationId = locationId,
                Code = code,
                PowerKw = request.PowerKw
            });
            created.Add(code);
        }

        if (created.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            if (cache is not null) await cache.InvalidateLocationAsync(locationId, ct);
            if (notifier is not null) await notifier.NotifyStationInventoryChangedAsync(locationId, ct);
        }

        return StatusCode(StatusCodes.Status201Created, new BatchProvisionResult(created.Count, skipped, created));
    }

    [HttpPost("locations/{locationId:guid}/batteries/batch")]
    public async Task<ActionResult<BatchProvisionResult>> BatchCreateBatteries(
        Guid locationId, BatchCreateBatteriesRequest request, CancellationToken ct)
    {
        var location = await db.Locations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == locationId, ct);
        if (location is null) return NotFound();
        if (!location.OffersBatterySwap)
            return Problem(statusCode: StatusCodes.Status409Conflict, detail: "Trạm này chưa bật dịch vụ đổi pin.");

        var prefix = request.Prefix.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(prefix))
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: "Tiền tố serial pin không được để trống.");

        var candidateSerials = Enumerable.Range(0, request.Count)
            .Select(i => $"{prefix}{(request.StartNumber + i):D2}")
            .ToList();

        if (candidateSerials.Any(s => s.Length > 64))
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: "Mã serial pin vượt quá 64 ký tự.");

        var upperCandidates = candidateSerials.Select(s => s.ToUpperInvariant()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingSerials = (await db.Batteries.AsNoTracking()
            .Select(x => x.Serial)
            .ToListAsync(ct))
            .Where(s => upperCandidates.Contains(s))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var created = new List<string>();
        var skipped = 0;

        foreach (var serial in candidateSerials)
        {
            if (existingSerials.Contains(serial))
            {
                skipped++;
                continue;
            }

            existingSerials.Add(serial);
            db.Batteries.Add(new Battery
            {
                LocationId = locationId,
                Serial = serial,
                State = BatteryState.Ready
            });
            created.Add(serial);
        }

        if (created.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            if (cache is not null) await cache.InvalidateLocationAsync(locationId, ct);
            if (notifier is not null) await notifier.NotifyStationInventoryChangedAsync(locationId, ct);
        }

        return StatusCode(StatusCodes.Status201Created, new BatchProvisionResult(created.Count, skipped, created));
    }

    [HttpPut("locations/{locationId:guid}/service-hours/preset")]
    public async Task<IActionResult> ApplyServiceHoursPreset(
        Guid locationId, ApplyServiceHoursPresetRequest request, CancellationToken ct)
    {
        if (request.OpensAtMinute >= request.ClosesAtMinute)
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: "Giờ mở cửa phải trước giờ đóng cửa.");

        var location = await db.Locations.SingleOrDefaultAsync(x => x.Id == locationId, ct);
        if (location is null) return NotFound();

        var services = new List<ReservationServiceType>();
        if (location.OffersCarCharging) services.Add(ReservationServiceType.Charging);
        if (location.OffersBatterySwap) services.Add(ReservationServiceType.BatterySwap);
        if (services.Count == 0)
            return Problem(statusCode: StatusCodes.Status409Conflict, detail: "Trạm chưa cấu hình dịch vụ nào.");

        var existing = await db.Set<ServiceHours>()
            .Where(x => x.LocationId == locationId)
            .ToListAsync(ct);
        db.Set<ServiceHours>().RemoveRange(existing);

        foreach (var serviceType in services)
        {
            for (var day = 0; day < 7; day++)
            {
                db.Set<ServiceHours>().Add(new ServiceHours
                {
                    LocationId = locationId,
                    ServiceType = serviceType,
                    Day = (DayOfWeek)day,
                    OpensAtMinute = request.OpensAtMinute,
                    ClosesAtMinute = request.ClosesAtMinute
                });
            }
        }

        var isOpen24Hours = request.OpensAtMinute == 0 && request.ClosesAtMinute == 1440;
        location.SetServiceDetails(
            isOpen24Hours,
            location.ChargingPowerKw,
            location.BatteryCapacityKwh,
            location.AvailableBatteries,
            location.AvailableChargingBays);

        await db.SaveChangesAsync(ct);
        if (cache is not null) await cache.InvalidateLocationAsync(locationId, ct);
        if (notifier is not null) await notifier.NotifyStationInventoryChangedAsync(locationId, ct);
        return NoContent();
    }

    [HttpGet("operators")]
    public async Task<ActionResult<IReadOnlyList<AdminOperatorSummary>>> ListOperators(CancellationToken ct)
    {
        var operators = await userManager.GetUsersInRoleAsync(AppRoles.Operator);
        var operatorIds = operators.Select(x => x.Id).ToHashSet();

        var activeAssignments = operatorIds.Count == 0
            ? new Dictionary<Guid, (Guid LocationId, string LocationName, DateTimeOffset? AssignedAtUtc)>()
            : (await (
                from assignment in db.Set<OperatorAssignment>().AsNoTracking()
                join location in db.Locations.AsNoTracking() on assignment.LocationId equals location.Id
                where assignment.EndedAtUtc == null && operatorIds.Contains(assignment.OperatorId)
                select new { assignment.OperatorId, assignment.LocationId, location.Name, assignment.AssignedAtUtc }
            ).ToListAsync(ct))
            .GroupBy(x => x.OperatorId)
            .ToDictionary(g => g.Key, g =>
            {
                var first = g.OrderByDescending(x => x.AssignedAtUtc).First();
                return (first.LocationId, LocationName: first.Name, first.AssignedAtUtc);
            });

        var result = operators
            .OrderBy(x => x.FullName)
            .Select(op =>
            {
                var hasAssignment = activeAssignments.TryGetValue(op.Id, out var a);
                return new AdminOperatorSummary(
                    op.Id,
                    op.FullName,
                    op.Email ?? string.Empty,
                    op.PhoneNumber,
                    op.IsActive,
                    hasAssignment ? a.LocationId : null,
                    hasAssignment ? a.LocationName : null,
                    hasAssignment ? a.AssignedAtUtc : null);
            })
            .ToList();

        return Ok(result);
    }

    [HttpGet("reports/summary")]
    public async Task<ActionResult<AdminReportsSummaryResponse>> GetReportsSummary(
        [FromQuery] Guid? locationId,
        [FromQuery] DateTimeOffset? fromUtc,
        [FromQuery] DateTimeOffset? toUtc,
        CancellationToken ct)
    {
        if (fromUtc.HasValue && toUtc.HasValue && fromUtc.Value > toUtc.Value)
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: "Thời gian bắt đầu không được sau thời gian kết thúc.");

        var now = _clock.GetUtcNow();

        var locationsQuery = db.Locations.AsNoTracking();
        if (locationId.HasValue)
            locationsQuery = locationsQuery.Where(x => x.Id == locationId.Value);

        var locations = await locationsQuery.OrderBy(x => x.Name).ToListAsync(ct);
        var locIds = locations.Select(x => x.Id).ToHashSet();

        var bays = await db.ChargingBays.AsNoTracking()
            .Where(x => locIds.Contains(x.LocationId))
            .Select(x => new { x.LocationId, x.EquipmentState, x.ParkingState })
            .ToListAsync(ct);

        var batteries = await db.Batteries.AsNoTracking()
            .Where(x => x.LocationId.HasValue && locIds.Contains(x.LocationId.Value))
            .Select(x => new { LocationId = x.LocationId!.Value, x.State })
            .ToListAsync(ct);

        var useSqlDateFilter = db.Database.IsNpgsql();

        var sessionsQuery =
            from s in db.Set<ChargingSession>().AsNoTracking()
            join r in db.Reservations.AsNoTracking() on s.ReservationId equals r.Id
            where locIds.Contains(r.LocationId)
            select new { r.LocationId, s.StartedAtUtc, s.StartMeterKwh, s.EndMeterKwh };

        if (useSqlDateFilter)
        {
            if (fromUtc.HasValue) sessionsQuery = sessionsQuery.Where(x => x.StartedAtUtc >= fromUtc.Value);
            if (toUtc.HasValue) sessionsQuery = sessionsQuery.Where(x => x.StartedAtUtc <= toUtc.Value);
        }

        var sessionsRaw = await sessionsQuery.ToListAsync(ct);
        var sessions = useSqlDateFilter
            ? sessionsRaw
            : sessionsRaw
                .Where(x => (!fromUtc.HasValue || x.StartedAtUtc >= fromUtc.Value) &&
                            (!toUtc.HasValue || x.StartedAtUtc <= toUtc.Value))
                .ToList();

        var swapsQuery =
            from sw in db.BatterySwaps.AsNoTracking()
            join r in db.Reservations.AsNoTracking() on sw.ReservationId equals r.Id
            where locIds.Contains(r.LocationId)
            select new { r.LocationId, sw.CompletedAtUtc };

        if (useSqlDateFilter)
        {
            if (fromUtc.HasValue) swapsQuery = swapsQuery.Where(x => x.CompletedAtUtc >= fromUtc.Value);
            if (toUtc.HasValue) swapsQuery = swapsQuery.Where(x => x.CompletedAtUtc <= toUtc.Value);
        }

        var swapsRaw = await swapsQuery.ToListAsync(ct);
        var swaps = useSqlDateFilter
            ? swapsRaw
            : swapsRaw
                .Where(x => (!fromUtc.HasValue || x.CompletedAtUtc >= fromUtc.Value) &&
                            (!toUtc.HasValue || x.CompletedAtUtc <= toUtc.Value))
                .ToList();

        var invoicesQuery =
            from inv in db.Invoices.AsNoTracking()
            join r in db.Reservations.AsNoTracking() on inv.ReservationId equals r.Id
            where locIds.Contains(r.LocationId)
            select new { r.LocationId, inv.TotalAmount, inv.TotalKwh, inv.Status, inv.CreatedAtUtc, inv.DueAtUtc };

        if (useSqlDateFilter)
        {
            if (fromUtc.HasValue) invoicesQuery = invoicesQuery.Where(x => x.CreatedAtUtc >= fromUtc.Value);
            if (toUtc.HasValue) invoicesQuery = invoicesQuery.Where(x => x.CreatedAtUtc <= toUtc.Value);
        }

        var invoicesRaw = await invoicesQuery.ToListAsync(ct);
        var invoices = useSqlDateFilter
            ? invoicesRaw
            : invoicesRaw
                .Where(x => (!fromUtc.HasValue || x.CreatedAtUtc >= fromUtc.Value) &&
                            (!toUtc.HasValue || x.CreatedAtUtc <= toUtc.Value))
                .ToList();

        var incidentsQuery = db.Set<IncidentReport>().AsNoTracking()
            .Where(x => locIds.Contains(x.LocationId))
            .Select(x => new { x.LocationId, x.ReportedAtUtc, x.ResolvedAtUtc });

        if (useSqlDateFilter)
        {
            if (fromUtc.HasValue) incidentsQuery = incidentsQuery.Where(x => x.ReportedAtUtc >= fromUtc.Value);
            if (toUtc.HasValue) incidentsQuery = incidentsQuery.Where(x => x.ReportedAtUtc <= toUtc.Value);
        }

        var incidentsRaw = await incidentsQuery.ToListAsync(ct);
        var incidents = useSqlDateFilter
            ? incidentsRaw
            : incidentsRaw
                .Where(x => (!fromUtc.HasValue || x.ReportedAtUtc >= fromUtc.Value) &&
                            (!toUtc.HasValue || x.ReportedAtUtc <= toUtc.Value))
                .ToList();

        var byLocation = locations.Select(loc =>
        {
            var locSessions = sessions.Where(x => x.LocationId == loc.Id).ToList();
            var locSwaps = swaps.Where(x => x.LocationId == loc.Id).ToList();
            var locInvoices = invoices.Where(x => x.LocationId == loc.Id).ToList();
            var locBays = bays.Where(x => x.LocationId == loc.Id).ToList();
            var locBatteries = batteries.Where(x => x.LocationId == loc.Id).ToList();
            var locIncidents = incidents.Where(x => x.LocationId == loc.Id).ToList();

            var kwh = locSessions.Sum(s => s.EndMeterKwh.HasValue && s.EndMeterKwh.Value >= s.StartMeterKwh
                ? s.EndMeterKwh.Value - s.StartMeterKwh
                : 0m);

            return new AdminLocationReportItem(
                loc.Id,
                loc.Name,
                loc.IsActive,
                loc.OperatingStatus.ToString(),
                locSessions.Count,
                kwh,
                locSwaps.Count,
                locInvoices.Where(i => i.Status == InvoiceStatus.Paid).Sum(i => i.TotalAmount),
                locInvoices.Where(i => i.Status == InvoiceStatus.Pending).Sum(i => i.TotalAmount),
                locBays.Count,
                locBays.Count(b => b.ParkingState == ParkingState.Occupied),
                locBays.Count(b => b.EquipmentState == ChargerState.Faulted),
                locBatteries.Count,
                locBatteries.Count(b => b.State == BatteryState.Ready),
                locIncidents.Count,
                locIncidents.Count(i => !i.ResolvedAtUtc.HasValue));
        }).ToList();

        var totalBays = byLocation.Sum(x => x.TotalBays);
        var occupiedBays = byLocation.Sum(x => x.OccupiedBays);
        var utilization = totalBays > 0 ? Math.Round((decimal)occupiedBays * 100m / totalBays, 1) : 0m;
        var overdueCount = invoices.Count(i => i.Status == InvoiceStatus.Pending && i.DueAtUtc.HasValue && i.DueAtUtc.Value < now);

        return Ok(new AdminReportsSummaryResponse(
            fromUtc,
            toUtc,
            locations.Count,
            locations.Count(x => x.IsActive),
            byLocation.Sum(x => x.ChargingSessionCount),
            byLocation.Sum(x => x.TotalKwh),
            byLocation.Sum(x => x.BatterySwapCount),
            byLocation.Sum(x => x.PaidRevenue),
            byLocation.Sum(x => x.PendingAmount),
            overdueCount,
            totalBays,
            occupiedBays,
            byLocation.Sum(x => x.FaultedBays),
            utilization,
            byLocation.Sum(x => x.IncidentCount),
            byLocation.Sum(x => x.OpenIncidentCount),
            byLocation));
    }
}
