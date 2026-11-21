using EVChargeSwap.Data;
using EVChargeSwap.Features.Reservations.Entities;
using Microsoft.EntityFrameworkCore;

namespace EVChargeSwap.Features.Reservations.Services;

internal static class TariffLookup
{
    public const decimal LegacyChargingRate = 3858m;

    public static Task<decimal?> FindAsync(AppDbContext db, Guid station, ReservationServiceType service,
        DateTimeOffset now, CancellationToken ct) =>
        db.Tariffs.Where(x => x.LocationId == station && x.ServiceType == service && x.EffectiveFromUtc <= now)
            .OrderByDescending(x => x.EffectiveFromUtc).Select(x => (decimal?)x.UnitPrice).FirstOrDefaultAsync(ct);
}
