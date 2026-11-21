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

[ApiController]
[Authorize(Roles = AppRoles.Operator)]
[Route("api/operator/me/tariffs")]
public sealed class TariffsController(AppDbContext db, TimeProvider clock) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var station = await StationAsync(ct);
        if (station is null) return NotFound();
        return Ok(await db.Tariffs.AsNoTracking().Where(x => x.LocationId == station)
            .OrderByDescending(x => x.EffectiveFromUtc).Take(50).ToListAsync(ct));
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateTariffRequest request, CancellationToken ct)
    {
        var maximum = request.ServiceType == ReservationServiceType.Charging ? 10_000m : 999_999m;
        if (!Enum.IsDefined(request.ServiceType) || request.UnitPrice < 0 || request.UnitPrice > maximum ||
            decimal.Round(request.UnitPrice, 2) != request.UnitPrice)
            return Problem(statusCode: 400, detail: "Invalid service or unit price.");

        var station = await StationAsync(ct);
        if (station is not { } id) return NotFound();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (!await ReservationLocks.LockOperatorStationAsync(db, id, UserId(), ct)) return NotFound();
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
            EffectiveFromUtc = effective, CreatedAtUtc = now, CreatedById = UserId() };
        db.Tariffs.Add(tariff);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return StatusCode(201, tariff);
    }

    private Guid UserId() => Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

    private Task<Guid?> StationAsync(CancellationToken ct) => db.Set<OperatorAssignment>()
        .Where(x => x.OperatorId == UserId() && x.EndedAtUtc == null)
        .Select(x => (Guid?)x.LocationId).SingleOrDefaultAsync(ct);
}
