using System.Text.Json;
using EVChargeSwap.Common.Messaging.Events;
using EVChargeSwap.Features.Locations.Services;
using EVChargeSwap.Features.Users.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace EVChargeSwap.Common.Messaging.Consumers;

public sealed class ReservationRealtimeConsumer(
    IHubContext<NotificationHub> hub, IStationNotifier stationNotifier) : IConsumer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public string ConsumerName => nameof(ReservationRealtimeConsumer);
    public string QueueName => "ev.reservations.realtime.queue";
    public IReadOnlyList<string> RoutingKeys { get; } =
    [
        EventRoutingKeys.ReservationConfirmed, EventRoutingKeys.ReservationCancelled,
        EventRoutingKeys.ReservationExpired, EventRoutingKeys.CustomerCheckedIn,
        EventRoutingKeys.ReservationCompleted, EventRoutingKeys.ReservationDetailsChanged
    ];

    public async Task HandleAsync(string eventType, string routingKey, string payload, Guid messageId,
        CancellationToken ct)
    {
        var change = JsonSerializer.Deserialize<ReservationChangedEvent>(payload, JsonOptions);
        if (change is null || change.ReservationId == Guid.Empty || change.CustomerId == Guid.Empty ||
            change.LocationId == Guid.Empty)
            throw new JsonException("Reservation update is missing its routing identifiers.");

        await hub.Clients.Group(NotificationHub.GroupFor(change.CustomerId))
            .SendAsync("ReservationChanged", change.ReservationId, ct);
        await stationNotifier.NotifyStationInventoryChangedAsync(change.LocationId, ct);
    }
}
