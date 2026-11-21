using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EVChargeSwap.Data;
using EVChargeSwap.Data.Entities;
using Microsoft.Extensions.Logging;

namespace EVChargeSwap.Common.Messaging;

public interface IOutboxService
{
    Task EnqueueAsync<T>(string eventType, string routingKey, T payload, CancellationToken ct = default) where T : class;
}

public sealed class OutboxService : IOutboxService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly AppDbContext _db;
    private readonly ILogger<OutboxService> _logger;

    public OutboxService(AppDbContext db, ILogger<OutboxService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public Task EnqueueAsync<T>(string eventType, string routingKey, T payload, CancellationToken ct = default) where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(routingKey);
        ArgumentNullException.ThrowIfNull(payload);

        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        var traceParent = Activity.Current?.Id;

        var outboxMessage = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = eventType,
            RoutingKey = routingKey,
            Payload = payloadJson,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ProcessedAtUtc = null,
            RetryCount = 0,
            LastError = null,
            TraceParent = traceParent
        };

        _db.OutboxMessages.Add(outboxMessage);
        _logger.LogDebug("Enqueued outbox message {Id} for event '{EventType}' with routing key '{RoutingKey}'.",
            outboxMessage.Id, eventType, routingKey);

        return Task.CompletedTask;
    }
}
