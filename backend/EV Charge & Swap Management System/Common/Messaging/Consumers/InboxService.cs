using System;
using System.Threading;
using System.Threading.Tasks;
using EVChargeSwap.Data;
using EVChargeSwap.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace EVChargeSwap.Common.Messaging.Consumers;

public interface IInboxService
{
    Task<bool> HasBeenProcessedAsync(Guid messageId, string consumerName, CancellationToken ct = default);
    Task<bool> TryClaimAsync(Guid messageId, string consumerName, CancellationToken ct = default);
    Task MarkAsReceivedAsync(Guid messageId, string consumerName, CancellationToken ct = default);
    Task MarkAsProcessedAsync(Guid messageId, string consumerName, CancellationToken ct = default);
}

public sealed class InboxService : IInboxService
{
    private readonly AppDbContext _db;
    private readonly ILogger<InboxService> _logger;
    private readonly TimeProvider _clock;

    public InboxService(AppDbContext db, ILogger<InboxService> logger, TimeProvider? clock = null)
    {
        _db = db;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<bool> HasBeenProcessedAsync(Guid messageId, string consumerName, CancellationToken ct = default)
    {
        return await _db.InboxMessages
            .AsNoTracking()
            .AnyAsync(x => x.Id == messageId && x.ConsumerName == consumerName && x.ProcessedAtUtc != null, ct);
    }

    public async Task<bool> TryClaimAsync(Guid messageId, string consumerName, CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow();
        if (_db.Database.IsNpgsql())
        {
            var inserted = await _db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO inbox_messages ("Id", "ConsumerName", "ReceivedAtUtc", "ProcessedAtUtc")
                VALUES ({messageId}, {consumerName}, {now}, NULL)
                ON CONFLICT ("Id", "ConsumerName") DO NOTHING
                """, ct);
            if (inserted == 1) return true;

            var alreadyProcessed = await HasBeenProcessedAsync(messageId, consumerName, ct);
            return !alreadyProcessed;
        }

        var existing = await _db.InboxMessages
            .SingleOrDefaultAsync(x => x.Id == messageId && x.ConsumerName == consumerName, ct);
        if (existing is not null)
        {
            return existing.ProcessedAtUtc is null;
        }

        _db.InboxMessages.Add(new InboxMessage
        {
            Id = messageId,
            ConsumerName = consumerName,
            ReceivedAtUtc = now,
            ProcessedAtUtc = null
        });

        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            _db.ChangeTracker.Clear();
            return !await HasBeenProcessedAsync(messageId, consumerName, ct);
        }
    }

    public async Task MarkAsReceivedAsync(Guid messageId, string consumerName, CancellationToken ct = default)
    {
        await TryClaimAsync(messageId, consumerName, ct);
    }

    public async Task MarkAsProcessedAsync(Guid messageId, string consumerName, CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow();
        var updated = await _db.InboxMessages
            .Where(x => x.Id == messageId && x.ConsumerName == consumerName)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ProcessedAtUtc, now), ct);

        if (updated == 0)
        {
            _db.InboxMessages.Add(new InboxMessage
            {
                Id = messageId,
                ConsumerName = consumerName,
                ReceivedAtUtc = now,
                ProcessedAtUtc = now
            });
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                _db.ChangeTracker.Clear();
                await _db.InboxMessages
                    .Where(x => x.Id == messageId && x.ConsumerName == consumerName && x.ProcessedAtUtc == null)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ProcessedAtUtc, now), ct);
            }
        }

        _logger.LogDebug("Marked message {Id} as processed by consumer '{Consumer}'.", messageId, consumerName);
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }
        || ex.InnerException?.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) == true;
}
