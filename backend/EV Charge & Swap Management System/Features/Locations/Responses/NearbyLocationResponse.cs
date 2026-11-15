namespace EVChargeSwap.Features.Locations.Responses;

public sealed record NearbyLocationResponse(LocationResponse Location, double DistanceKm);
