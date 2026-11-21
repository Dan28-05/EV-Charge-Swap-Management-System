namespace EVChargeSwap.Features.Reservations.Entities;

public enum IncidentResolution { Reassigned = 1, Cancelled, Recorded }

public sealed class IncidentReport
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid LocationId { get; init; }
    public Guid? ChargingBayId { get; init; }
    public Guid? BatteryId { get; init; }
    public Guid? ReservationId { get; init; }
    public Guid? ChargingSessionId { get; init; }
    public Guid ReportedById { get; init; }
    public DateTimeOffset ReportedAtUtc { get; init; }
    public required string Description { get; init; }
    public IncidentResolution? Resolution { get; private set; }
    public Guid? ResolvedById { get; private set; }
    public DateTimeOffset? ResolvedAtUtc { get; private set; }
    public Guid? ReplacementChargingBayId { get; private set; }
    public Guid? ReplacementBatteryId { get; private set; }

    internal void Resolve(IncidentResolution resolution, Guid operatorId, DateTimeOffset now, Guid? replacementId)
    {
        Resolution = resolution;
        ResolvedById = operatorId;
        ResolvedAtUtc = now;
        ReplacementChargingBayId = ChargingBayId.HasValue ? replacementId : null;
        ReplacementBatteryId = BatteryId.HasValue ? replacementId : null;
    }
}
