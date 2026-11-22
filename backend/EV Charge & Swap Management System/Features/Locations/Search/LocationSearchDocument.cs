namespace EVChargeSwap.Features.Locations.Search;

internal sealed record LocationSearchPoint(decimal Lat, decimal Lon);

internal sealed record LocationSearchDocument(
    Guid Id, string Name, string Address, LocationSearchPoint Location);

internal sealed record LocationSearchPage(IReadOnlyList<Guid> Ids, int TotalCount);
