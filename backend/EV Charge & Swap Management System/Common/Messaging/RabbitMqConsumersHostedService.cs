using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EVChargeSwap.Common.Messaging.Consumers;
using EVChargeSwap.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace EVChargeSwap.Common.Messaging;

public sealed class RabbitMqConsumersHostedService : BackgroundService
{
    private const string DeadLetterExchange = RabbitMqConnectionProvider.DeadLetterExchange;
    private const string RetryCountHeader = "x-retry-count";
    private const int MaxConsumerRetries = 3;
    internal static readonly ActivitySource ActivitySource = new("EVChargeSwap.Messaging");

    internal static IReadOnlyList<Type> ConsumerTypes { get; } =
    [
        typeof(NotificationConsumer),
        typeof(EmailDeliveryConsumer),
        typeof(BillingConsumer),
        typeof(InvoiceRealtimeConsumer),
        typeof(ReservationRealtimeConsumer),
        typeof(LocationUpdatedConsumer)
    ];

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRabbitMqConnectionProvider _connectionProvider;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqConsumersHostedService> _logger;
    private readonly List<(IChannel Channel, string ConsumerTag)> _activeConsumers = [];

    public RabbitMqConsumersHostedService(
        IServiceScopeFactory scopeFactory,
        IRabbitMqConnectionProvider connectionProvider,
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqConsumersHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _connectionProvider = connectionProvider;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled || !_connectionProvider.IsConfigured)
        {
            _logger.LogInformation("RabbitMQ consumers service is disabled or not configured.");
            return;
        }

        var backoffDelay = TimeSpan.FromSeconds(2);
        var maxBackoff = TimeSpan.FromSeconds(30);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await StartConsumersAsync(stoppingToken);
                _logger.LogInformation("All RabbitMQ enterprise consumers started and listening.");
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                await CloseActiveConsumersAsync();
                _logger.LogWarning("RabbitMQ broker not ready for consumers ({Message}). Retrying in {Delay}s...",
                    ex.Message, backoffDelay.TotalSeconds);

                try
                {
                    await Task.Delay(backoffDelay, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                backoffDelay = TimeSpan.FromSeconds(Math.Min(backoffDelay.TotalSeconds * 2, maxBackoff.TotalSeconds));
            }
        }

        _logger.LogInformation("RabbitMQ consumers background service stopped.");
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await CloseActiveConsumersAsync();
        await base.StopAsync(cancellationToken);
    }

    private async Task StartConsumersAsync(CancellationToken ct)
    {
        await _connectionProvider.EnsureTopologyAsync(ct);

        foreach (var consumerType in ConsumerTypes)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var consumerInstance = (IConsumer)scope.ServiceProvider.GetRequiredService(consumerType);

            var channel = await _connectionProvider.CreateChannelAsync(ct);
            try
            {
                await DeclareConsumerQueueAsync(channel, consumerInstance, _options.ExchangeName, ct);
                await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 10, global: false, cancellationToken: ct);

                var consumer = new AsyncEventingBasicConsumer(channel);
                var queueName = consumerInstance.QueueName;
                var consumerName = consumerInstance.ConsumerName;

                consumer.ReceivedAsync += async (_, ea) =>
                {
                    using var activity = StartConsumerActivity(consumerName, ea);
                    try
                    {
                        if (!Guid.TryParse(ea.BasicProperties?.MessageId, out var messageId) || messageId == Guid.Empty)
                        {
                            _logger.LogError("Rejected message without valid MessageId in {Consumer}. Routing to DLQ.", consumerName);
                            await SafeNackAsync(channel, ea.DeliveryTag, requeue: false);
                            return;
                        }

                        await using var msgScope = _scopeFactory.CreateAsyncScope();
                        var db = msgScope.ServiceProvider.GetRequiredService<AppDbContext>();
                        var inboxService = msgScope.ServiceProvider.GetRequiredService<IInboxService>();
                        var currentConsumer = (IConsumer)msgScope.ServiceProvider.GetRequiredService(consumerType);

                        var body = ea.Body.ToArray();
                        var payload = Encoding.UTF8.GetString(body);
                        var eventType = ea.BasicProperties?.Type ?? string.Empty;

                        await using var tx = await db.Database.BeginTransactionAsync(ct);
                        var claimed = await inboxService.TryClaimAsync(messageId, consumerName, ct);
                        if (!claimed)
                        {
                            await tx.CommitAsync(ct);
                            _logger.LogInformation("Message {MessageId} already processed by {Consumer}. Skipping duplicate.",
                                messageId, consumerName);
                            await SafeAckAsync(channel, ea.DeliveryTag);
                            return;
                        }

                        await currentConsumer.HandleAsync(eventType, ea.RoutingKey, payload, messageId, ct);
                        await inboxService.MarkAsProcessedAsync(messageId, consumerName, ct);
                        await tx.CommitAsync(ct);

                        await SafeAckAsync(channel, ea.DeliveryTag);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        // Graceful shutdown: requeue so in-flight messages are not permanently dead-lettered.
                        _logger.LogInformation("Requeueing in-flight message {MessageId} in {Consumer} due to host shutdown.",
                            ea.BasicProperties?.MessageId, consumerName);
                        await SafeNackAsync(channel, ea.DeliveryTag, requeue: true);
                    }
                    catch (Exception ex)
                    {
                        var retryCount = GetRetryCount(ea.BasicProperties);
                        if (retryCount < MaxConsumerRetries)
                        {
                            _logger.LogWarning(ex,
                                "Failed to process message {MessageId} in {Consumer} (attempt {Attempt}/{Max}). Scheduling retry.",
                                ea.BasicProperties?.MessageId, consumerName, retryCount + 1, MaxConsumerRetries);

                            try
                            {
                                await Task.Delay(TimeSpan.FromMilliseconds(500 * (retryCount + 1)), ct);
                                var retryProps = new BasicProperties
                                {
                                    MessageId = ea.BasicProperties?.MessageId,
                                    Type = ea.BasicProperties?.Type,
                                    ContentType = ea.BasicProperties?.ContentType ?? "application/json",
                                    DeliveryMode = DeliveryModes.Persistent,
                                    Headers = new Dictionary<string, object?>(ea.BasicProperties?.Headers ?? new Dictionary<string, object?>())
                                    {
                                        [RetryCountHeader] = retryCount + 1
                                    }
                                };

                                // Republish directly to this consumer's queue with incremented retry count.
                                await channel.BasicPublishAsync(
                                    exchange: string.Empty,
                                    routingKey: queueName,
                                    mandatory: false,
                                    basicProperties: retryProps,
                                    body: ea.Body,
                                    cancellationToken: ct);
                                await SafeAckAsync(channel, ea.DeliveryTag);
                                return;
                            }
                            catch (OperationCanceledException) when (ct.IsCancellationRequested)
                            {
                                await SafeNackAsync(channel, ea.DeliveryTag, requeue: true);
                                return;
                            }
                            catch (Exception retryEx)
                            {
                                _logger.LogError(retryEx, "Retry republish failed for message {MessageId} in {Consumer}. Routing to DLQ.",
                                    ea.BasicProperties?.MessageId, consumerName);
                            }
                        }
                        else
                        {
                            _logger.LogError(ex, "Exhausted {Max} retries for message {MessageId} in {Consumer}. Routing to DLQ.",
                                MaxConsumerRetries, ea.BasicProperties?.MessageId, consumerName);
                        }

                        await SafeNackAsync(channel, ea.DeliveryTag, requeue: false);
                    }
                };

                var consumerTag = await channel.BasicConsumeAsync(
                    queue: queueName,
                    autoAck: false,
                    consumer: consumer,
                    cancellationToken: ct);

                lock (_activeConsumers)
                {
                    _activeConsumers.Add((channel, consumerTag));
                }
                _logger.LogInformation("Consumer {Consumer} registered on queue '{Queue}'.", consumerName, queueName);
            }
            catch
            {
                await channel.DisposeAsync();
                throw;
            }
        }
    }

    internal static async Task DeclareConsumerQueueAsync(IChannel channel, IConsumer consumer, string exchange,
        CancellationToken ct)
    {
        await channel.QueueDeclareAsync(consumer.QueueName, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = DeadLetterExchange,
                ["x-dead-letter-routing-key"] = "dlq"
            }, cancellationToken: ct);
        foreach (var key in consumer.RoutingKeys)
            await channel.QueueBindAsync(consumer.QueueName, exchange, key, arguments: null, cancellationToken: ct);
    }

    private static Activity? StartConsumerActivity(string consumerName, BasicDeliverEventArgs ea)
    {
        ActivityContext parentContext = default;
        if (ea.BasicProperties?.Headers is not null &&
            ea.BasicProperties.Headers.TryGetValue("traceparent", out var rawTrace) &&
            rawTrace is not null)
        {
            var traceParent = rawTrace switch
            {
                byte[] bytes => Encoding.UTF8.GetString(bytes),
                string text => text,
                _ => rawTrace.ToString()
            };
            if (!string.IsNullOrWhiteSpace(traceParent))
                ActivityContext.TryParse(traceParent, null, out parentContext);
        }

        var activity = parentContext != default
            ? ActivitySource.StartActivity($"consume {ea.RoutingKey}", ActivityKind.Consumer, parentContext)
            : ActivitySource.StartActivity($"consume {ea.RoutingKey}", ActivityKind.Consumer);
        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.destination.name", ea.RoutingKey);
        activity?.SetTag("messaging.consumer.name", consumerName);
        if (!string.IsNullOrWhiteSpace(ea.BasicProperties?.MessageId))
            activity?.SetTag("messaging.message.id", ea.BasicProperties.MessageId);
        return activity;
    }

    private static int GetRetryCount(IReadOnlyBasicProperties? properties)
    {
        if (properties?.Headers is null || !properties.Headers.TryGetValue(RetryCountHeader, out var raw) || raw is null)
            return 0;

        return raw switch
        {
            int i => i,
            long l => (int)l,
            byte b => b,
            _ => int.TryParse(raw.ToString(), out var parsed) ? parsed : 0
        };
    }

    private async Task SafeAckAsync(IChannel channel, ulong deliveryTag)
    {
        try
        {
            if (channel.IsOpen)
                await channel.BasicAckAsync(deliveryTag, multiple: false);
        }
        catch (AlreadyClosedException)
        {
            // Broker will redeliver unacked messages automatically.
        }
    }

    private async Task SafeNackAsync(IChannel channel, ulong deliveryTag, bool requeue)
    {
        try
        {
            if (channel.IsOpen)
                await channel.BasicNackAsync(deliveryTag, multiple: false, requeue: requeue);
        }
        catch (AlreadyClosedException)
        {
            // Broker will redeliver unacked messages automatically.
        }
    }

    private async Task CloseActiveConsumersAsync()
    {
        List<(IChannel Channel, string ConsumerTag)> snapshot;
        lock (_activeConsumers)
        {
            snapshot = [.. _activeConsumers];
            _activeConsumers.Clear();
        }

        foreach (var (channel, consumerTag) in snapshot)
        {
            try
            {
                if (channel.IsOpen)
                    await channel.BasicCancelAsync(consumerTag);
            }
            catch
            {
                // Ignore cancel errors during shutdown.
            }

            try
            {
                await channel.DisposeAsync();
            }
            catch
            {
                // Ignore channel cleanup errors on shutdown.
            }
        }
    }
}
