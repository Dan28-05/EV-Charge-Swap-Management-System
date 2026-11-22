using System.Linq.Expressions;
using EVChargeSwap.Data;
using EVChargeSwap.Features.Locations.Entities;
using Microsoft.EntityFrameworkCore;

namespace EVChargeSwap.Features.Locations.Search;

public sealed class LocationIndexer(AppDbContext db, ElasticLocationIndex index, ILogger<LocationIndexer> logger)
{
    private const int BatchSize = 250;
    private const string LockKey = "location-search-index";

    private static readonly Expression<Func<Location, LocationSearchDocument>> ToDocument =
        location => new LocationSearchDocument(location.Id, location.Name, location.Address,
            new LocationSearchPoint(location.Latitude, location.Longitude));

    public bool Enabled => index.Enabled;

    public async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if ((await index.GetAliasTargetsAsync(ct)).Count == 0)
            await BuildAsync(onlyIfMissing: true, ct);
    }

    public async Task RebuildAsync(CancellationToken ct)
    {
        try
        {
            await BuildAsync(onlyIfMissing: false, ct);
        }
        catch (LocationIndexException ex)
        {
            logger.LogWarning("Station search rebuild failed: {Reason}", ex.Message);
            throw;
        }
    }

    public Task SynchronizeAsync(Guid locationId, CancellationToken ct) => WithIndexLockAsync(async () =>
    {
        var document = await db.Locations.AsNoTracking().Where(x => x.Id == locationId && x.IsActive)
            .Select(ToDocument).SingleOrDefaultAsync(ct);
        if (document is null)
            await index.DeleteAsync(locationId, ct);
        else
            await index.UpsertAsync(Normalize(document), ct);
    }, ct);

    private Task BuildAsync(bool onlyIfMissing, CancellationToken ct) => WithIndexLockAsync(async () =>
    {
        var previous = await index.GetAliasTargetsAsync(ct);
        if (onlyIfMissing && previous.Count > 0) return;

        var staging = await index.CreateIndexAsync(ct);
        var locations = db.Locations.AsNoTracking().Where(x => x.IsActive);
        Guid? after = null;
        var count = 0;
        while (true)
        {
            var page = after is { } id ? locations.Where(x => x.Id.CompareTo(id) > 0) : locations;
            var batch = await page.OrderBy(x => x.Id).Take(BatchSize).Select(ToDocument).ToListAsync(ct);
            if (batch.Count == 0) break;

            await index.BulkIndexAsync(staging, batch.Select(Normalize).ToArray(), ct);
            count += batch.Count;
            after = batch[^1].Id;
        }

        await index.PublishIndexAsync(staging, previous, ct);
        logger.LogInformation("Published station search index {Index} with {LocationCount} stations.", staging, count);
    }, ct);

    private async Task WithIndexLockAsync(Func<Task> action, CancellationToken ct)
    {
        // Serialize event writes with rebuilds across replicas. Business writes can continue and enqueue new events.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({LockKey}, 0))", ct);
        await action();
        await transaction.CommitAsync(ct);
    }

    private static LocationSearchDocument Normalize(LocationSearchDocument document) =>
        document with { Name = document.Name.Normalize(), Address = document.Address.Normalize() };
}
