namespace EVChargeSwap.Features.Locations.Services;

public sealed record StationInventoryUpdatedEvent(
    Guid LocationId,
    int? AvailableChargingBays,
    int? AvailableBatteries,
    DateTimeOffset UpdatedAtUtc
);

public interface IStationNotifier
{
    Task NotifyStationInventoryChangedAsync(Guid locationId, CancellationToken ct = default);
}
