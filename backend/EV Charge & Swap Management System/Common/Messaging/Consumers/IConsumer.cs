using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EVChargeSwap.Common.Messaging.Consumers;

public interface IConsumer
{
    string ConsumerName { get; }
    string QueueName { get; }
    IReadOnlyList<string> RoutingKeys { get; }
    Task HandleAsync(string eventType, string routingKey, string payload, Guid messageId, CancellationToken ct);
}
