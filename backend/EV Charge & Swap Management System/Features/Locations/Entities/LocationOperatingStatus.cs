using System.Text.Json.Serialization;

namespace EVChargeSwap.Features.Locations.Entities;

// A manually confirmed location-level state. Service hours and resource availability are separate.
[JsonConverter(typeof(JsonStringEnumConverter<LocationOperatingStatus>))]
public enum LocationOperatingStatus
{
    Unknown,
    Open,
    Closed,
    Maintenance
}
