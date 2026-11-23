using System.IdentityModel.Tokens.Jwt;
using EVChargeSwap.Data;
using EVChargeSwap.Features.Authorization;
using EVChargeSwap.Features.Reservations.Entities;
using EVChargeSwap.Features.Reservations.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EVChargeSwap.Features.Reservations;

public sealed record CreateTariffRequest(ReservationServiceType ServiceType, decimal UnitPrice,
    DateTimeOffset? EffectiveFromUtc);
public sealed record CurrentStationTariffsResponse(decimal? Charging, decimal? BatterySwap);

[ApiController]
[Authorize(Roles = AppRoles.Operator)]
[Route("api/operator/me/tariffs")]
public sealed class TariffsController(AppDbContext db, TimeProvider clock) : ControllerBase
{
    [HttpGet("current")]
    public async Task<ActionResult<CurrentStationTariffsResponse>> Current(CancellationToken ct)
    {
        if (!TryGetUserId(out var operatorId)) return Unauthorized();
        var station = await StationAsync(operatorId, ct);
        if (station is null) return NotFound();
        var location = await db.Locations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == station, ct);
        if (location is null) return NotFound();

        var now = clock.GetUtcNow();
        decimal? charging = location.OffersCarCharging
            ? await TariffLookup.FindAsync(db, location.Id, ReservationServiceType.Charging, now, ct)
                ?? TariffLookup.LegacyChargingRate
            : null;
        var batterySwap = location.OffersBatterySwap
            ? await TariffLookup.FindAsync(db, location.Id, ReservationServiceType.BatterySwap, now, ct)
            : null;
        return Ok(new CurrentStationTariffsResponse(charging, batterySwap));
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (!TryGetUserId(out var operatorId)) return Unauthorized();
        var station = await StationAsync(operatorId, ct);
        if (station is null) return NotFound();
        return Ok(await db.Tariffs.AsNoTracking().Where(x => x.LocationId == station)
            .OrderByDescending(x => x.EffectiveFromUtc).Take(50).ToListAsync(ct));
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateTariffRequest request, CancellationToken ct)
    {
        if (!TryGetUserId(out var operatorId)) return Unauthorized();
        var maximum = request.ServiceType == ReservationServiceType.Charging ? 10_000m : 999_999m;
        if (!Enum.IsDefined(request.ServiceType) || request.UnitPrice < 0 || request.UnitPrice > maximum ||
            decimal.Round(request.UnitPrice, 2) != request.UnitPrice)
            return Problem(statusCode: 400, detail: "Invalid service or unit price.");

        var station = await StationAsync(operatorId, ct);
        if (station is not { } id) return NotFound();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (!await ReservationLocks.LockOperatorStationAsync(db, id, operatorId, ct)) return NotFound();
        var location = await db.Locations.AsNoTracking().SingleAsync(x => x.Id == id, ct);
        if (request.ServiceType == ReservationServiceType.Charging ? !location.OffersCarCharging : !location.OffersBatterySwap)
            return Problem(statusCode: 409, detail: "The station does not offer this service.");

        var now = clock.GetUtcNow();
        var effective = request.EffectiveFromUtc?.ToUniversalTime() ?? now;
        if (effective < now)
            return Problem(statusCode: 400, detail: "Use a future effective time or omit it for immediate pricing.");
        if (await db.Tariffs.AnyAsync(x => x.LocationId == id && x.ServiceType == request.ServiceType &&
                x.EffectiveFromUtc == effective, ct))
            return Problem(statusCode: 409, detail: "A tariff already has this effective time.");

        var tariff = new Tariff { LocationId = id, ServiceType = request.ServiceType, UnitPrice = request.UnitPrice,
            EffectiveFromUtc = effective, CreatedAtUtc = now, CreatedById = operatorId };
        db.Tariffs.Add(tariff);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return StatusCode(201, tariff);
    }

    private bool TryGetUserId(out Guid operatorId) =>
        Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out operatorId);

    private Task<Guid?> StationAsync(Guid operatorId, CancellationToken ct) => db.Set<OperatorAssignment>()
        .Where(x => x.OperatorId == operatorId && x.EndedAtUtc == null)
        .Select(x => (Guid?)x.LocationId).SingleOrDefaultAsync(ct);
}
