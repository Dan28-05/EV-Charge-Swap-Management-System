using System;
using System.Threading;
using System.Threading.Tasks;
using EVChargeSwap.Data;
using EVChargeSwap.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EVChargeSwap.Common.Messaging.Consumers;

public interface IInboxService
{
    Task<bool> HasBeenProcessedAsync(Guid messageId, string consumerName, CancellationToken ct = default);
    Task MarkAsReceivedAsync(Guid messageId, string consumerName, CancellationToken ct = default);
    Task MarkAsProcessedAsync(Guid messageId, string consumerName, CancellationToken ct = default);
}

public sealed class InboxService : IInboxService
{
    private readonly AppDbContext _db;
    private readonly ILogger<InboxService> _logger;

    public InboxService(AppDbContext db, ILogger<InboxService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<bool> HasBeenProcessedAsync(Guid messageId, string consumerName, CancellationToken ct = default)
    {
        return await _db.InboxMessages
            .AnyAsync(x => x.Id == messageId && x.ConsumerName == consumerName && x.ProcessedAtUtc != null, ct);
    }

    public async Task MarkAsReceivedAsync(Guid messageId, string consumerName, CancellationToken ct = default)
    {
        var existing = await _db.InboxMessages
            .SingleOrDefaultAsync(x => x.Id == messageId && x.ConsumerName == consumerName, ct);

        if (existing is null)
        {
            _db.InboxMessages.Add(new InboxMessage
            {
                Id = messageId,
                ConsumerName = consumerName,
                ReceivedAtUtc = DateTimeOffset.UtcNow,
                ProcessedAtUtc = null
            });
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task MarkAsProcessedAsync(Guid messageId, string consumerName, CancellationToken ct = default)
    {
        var existing = await _db.InboxMessages
            .SingleOrDefaultAsync(x => x.Id == messageId && x.ConsumerName == consumerName, ct);

        if (existing is not null)
        {
            existing.ProcessedAtUtc = DateTimeOffset.UtcNow;
        }
        else
        {
            _db.InboxMessages.Add(new InboxMessage
            {
                Id = messageId,
                ConsumerName = consumerName,
                ReceivedAtUtc = DateTimeOffset.UtcNow,
                ProcessedAtUtc = DateTimeOffset.UtcNow
            });
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogDebug("Marked message {Id} as processed by consumer '{Consumer}'.", messageId, consumerName);
    }
}
