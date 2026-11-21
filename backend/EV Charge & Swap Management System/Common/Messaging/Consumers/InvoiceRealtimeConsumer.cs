using System.Text.Json;
using EVChargeSwap.Common.Messaging.Events;
using EVChargeSwap.Features.Reservations.Services;

namespace EVChargeSwap.Common.Messaging.Consumers;

public sealed class InvoiceRealtimeConsumer(InvoiceRealtime realtime) : IConsumer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public string ConsumerName => nameof(InvoiceRealtimeConsumer);
    public string QueueName => "ev.invoices.realtime.queue";
    public IReadOnlyList<string> RoutingKeys { get; } =
        [EventRoutingKeys.InvoiceGenerated, EventRoutingKeys.PaymentSucceeded];

    public Task HandleAsync(string eventType, string routingKey, string payload, Guid messageId,
        CancellationToken ct)
    {
        var change = JsonSerializer.Deserialize<PaymentSucceededEvent>(payload, JsonOptions)
            ?? throw new JsonException("Missing invoice refresh event.");
        if (change.InvoiceId == Guid.Empty) throw new JsonException("Missing invoice identifier.");
        return realtime.NotifyAsync(change.InvoiceId, ct);
    }
}
