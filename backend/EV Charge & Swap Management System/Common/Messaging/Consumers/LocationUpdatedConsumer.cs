using System.Text.Json;
using EVChargeSwap.Common.Messaging.Events;
using EVChargeSwap.Features.Locations.Search;

namespace EVChargeSwap.Common.Messaging.Consumers;

public sealed class LocationUpdatedConsumer(LocationIndexer indexer, ILogger<LocationUpdatedConsumer> logger) : IConsumer
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(12)];

    public string ConsumerName => nameof(LocationUpdatedConsumer);
    public string QueueName => "ev.location-search.queue";
    public IReadOnlyList<string> RoutingKeys => [EventRoutingKeys.LocationUpdated];

    public async Task HandleAsync(string eventType, string routingKey, string payload, Guid messageId, CancellationToken ct)
    {
        if (!indexer.Enabled) return;

        var message = JsonSerializer.Deserialize<LocationUpdatedEvent>(payload, Json);
        if (message is null || message.LocationId == Guid.Empty)
            throw new JsonException("Missing location ID in station search event.");

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await indexer.SynchronizeAsync(message.LocationId, ct);
                return;
            }
            catch (LocationIndexException ex) when (ex.IsTransient && attempt < RetryDelays.Length)
            {
                logger.LogWarning("Retrying station index update for message {MessageId}, attempt {Attempt}: {Reason}",
                    messageId, attempt + 1, ex.Message);
                await Task.Delay(RetryDelays[attempt], ct);
            }
        }
    }
}
