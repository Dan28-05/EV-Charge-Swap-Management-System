namespace EVChargeSwap.Features.Reservations.Entities;

public enum ReservationServiceType { Charging = 1, BatterySwap = 2 }
public enum ReservationStatus { Held = 1, CheckedIn, Completed, Cancelled, Expired }
public enum ChargerState { Active = 1, Faulted, Maintenance }
public enum ParkingState { Vacant = 1, Occupied }
public enum BatteryState { Ready = 1, WithCustomer, Inspection, Charging, Faulted }

public sealed class ChargingBay
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid LocationId { get; init; }
    public string Code { get; init; } = string.Empty;
    public int PowerKw { get; init; }
    public ChargerState EquipmentState { get; init; } = ChargerState.Active;
    public ParkingState ParkingState { get; init; } = ParkingState.Vacant;
    public Guid? StateUpdatedById { get; set; }
    public DateTimeOffset? StateUpdatedAtUtc { get; set; }
}

public sealed class Battery
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid? LocationId { get; init; }
    public string Serial { get; init; } = string.Empty;
    public Guid? CustodianCustomerId { get; set; }
    public BatteryState State { get; init; } = BatteryState.Inspection;
    public Guid? StateUpdatedById { get; set; }
    public DateTimeOffset? StateUpdatedAtUtc { get; set; }
}

// Overnight schedules can be entered as two intervals, ending/starting at midnight.
public sealed class ServiceHours
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid LocationId { get; init; }
    public ReservationServiceType ServiceType { get; init; }
    public DayOfWeek Day { get; init; }
    public int OpensAtMinute { get; init; }
    public int ClosesAtMinute { get; init; }
    public Guid? UpdatedById { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}

public sealed class OperatorAssignment
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid LocationId { get; init; }
    public Guid OperatorId { get; init; }
    public Guid? AssignedByAdminId { get; init; }
    public DateTimeOffset? AssignedAtUtc { get; init; }
    public DateTimeOffset? EndedAtUtc { get; set; }
}

public sealed class Reservation
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid CustomerId { get; init; }
    public Guid LocationId { get; init; }
    public Guid? ChargingBayId { get; init; }
    public Guid? BatteryId { get; init; }
    public string Code { get; init; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; init; }
    public Guid? IdempotencyKey { get; init; }
    public DateTimeOffset ExpiresAtUtc { get; init; }
    public ReservationStatus Status { get; private set; } = ReservationStatus.Held;
    public DateTimeOffset? CheckedInAtUtc { get; private set; }
    public Guid? CheckedInById { get; private set; }
    public DateTimeOffset? VehicleArrivedAtUtc { get; private set; }
    public Guid? VehicleArrivedById { get; private set; }
    public DateTimeOffset? VehicleDepartedAtUtc { get; private set; }
    public Guid? VehicleDepartedById { get; private set; }
    public DateTimeOffset? NoChargeConfirmedAtUtc { get; private set; }
    public Guid? NoChargeConfirmedById { get; private set; }
    public string? ClosingReason { get; private set; }
    public Guid? ClosedById { get; private set; }
    public DateTimeOffset? ClosedAtUtc { get; private set; }
}

public sealed class ChargingSession
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ReservationId { get; init; }
    public Guid ChargingBayId { get; init; }
    public Guid StartedById { get; init; }
    public DateTimeOffset StartedAtUtc { get; init; }
    public decimal StartMeterKwh { get; init; }
    public decimal? AppliedKwhRate { get; init; }
    public Guid? CompletedById { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public decimal? EndMeterKwh { get; private set; }
}
