using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EVChargeSwap.Common.Messaging.Consumers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace EVChargeSwap.Common.Messaging;

public sealed class RabbitMqConsumersHostedService : BackgroundService
{
    private const string DeadLetterExchange = "ev.dlx";
    private const string DeadLetterQueue = "ev.dlq";

    internal static IReadOnlyList<Type> ConsumerTypes { get; } =
    [
        typeof(NotificationConsumer),
        typeof(EmailDeliveryConsumer),
        typeof(BillingConsumer)
    ];

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRabbitMqConnectionProvider _connectionProvider;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqConsumersHostedService> _logger;
    private readonly List<IChannel> _activeChannels = [];

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
                // Keep background service alive while consumers process asynchronously
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("RabbitMQ broker not ready for consumers ({Message}). Retrying in {Delay}s...",
                    ex.Message, backoffDelay.TotalSeconds);

                await Task.Delay(backoffDelay, stoppingToken);
                backoffDelay = TimeSpan.FromSeconds(Math.Min(backoffDelay.TotalSeconds * 2, maxBackoff.TotalSeconds));
            }
        }

        _logger.LogInformation("RabbitMQ consumers background service stopped.");
    }

    private async Task StartConsumersAsync(CancellationToken ct)
    {
        await _connectionProvider.EnsureTopologyAsync(ct);

        // Declare Dead Letter Exchange and Queue
        using (var setupChannel = await _connectionProvider.CreateChannelAsync(ct))
        {
            await setupChannel.ExchangeDeclareAsync(
                exchange: DeadLetterExchange,
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false,
                arguments: null,
                cancellationToken: ct);

            await setupChannel.QueueDeclareAsync(
                queue: DeadLetterQueue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null,
                cancellationToken: ct);

            await setupChannel.QueueBindAsync(
                queue: DeadLetterQueue,
                exchange: DeadLetterExchange,
                routingKey: "#",
                arguments: null,
                cancellationToken: ct);
        }

        foreach (var consumerType in ConsumerTypes)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var consumerInstance = (IConsumer)scope.ServiceProvider.GetRequiredService(consumerType);

            var channel = await _connectionProvider.CreateChannelAsync(ct);
            _activeChannels.Add(channel);

            await DeclareConsumerQueueAsync(channel, consumerInstance, _options.ExchangeName, ct);

            await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 10, global: false, cancellationToken: ct);

            var consumer = new AsyncEventingBasicConsumer(channel);
            var queueName = consumerInstance.QueueName;
            var consumerName = consumerInstance.ConsumerName;

            consumer.ReceivedAsync += async (sender, ea) =>
            {
                try
                {
                    await using var msgScope = _scopeFactory.CreateAsyncScope();
                    var inboxService = msgScope.ServiceProvider.GetRequiredService<IInboxService>();
                    var currentConsumer = (IConsumer)msgScope.ServiceProvider.GetRequiredService(consumerType);

                    if (!Guid.TryParse(ea.BasicProperties?.MessageId, out var messageId))
                    {
                        messageId = Guid.NewGuid();
                    }

                    // Idempotent Inbox Check
                    if (await inboxService.HasBeenProcessedAsync(messageId, consumerName, ct))
                    {
                        _logger.LogInformation("Message {MessageId} already processed by {Consumer}. Skipping duplicate.",
                            messageId, consumerName);
                        await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
                        return;
                    }

                    var body = ea.Body.ToArray();
                    var payload = Encoding.UTF8.GetString(body);
                    var eventType = ea.BasicProperties?.Type ?? string.Empty;

                    await currentConsumer.HandleAsync(eventType, ea.RoutingKey, payload, messageId, ct);

                    await inboxService.MarkAsProcessedAsync(messageId, consumerName, ct);

                    await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process message {MessageId} in {Consumer}. Routing to DLQ.",
                        ea.BasicProperties?.MessageId, consumerName);

                    // Requeue: false forwards the message to dead-letter exchange (ev.dlx -> ev.dlq)
                    await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
                }
            };

            await channel.BasicConsumeAsync(
                queue: queueName,
                autoAck: false,
                consumer: consumer,
                cancellationToken: ct);

            _logger.LogInformation("Consumer {Consumer} registered on queue '{Queue}'.", consumerName, queueName);
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

    public override void Dispose()
    {
        foreach (var channel in _activeChannels)
        {
            try
            {
                channel.Dispose();
            }
            catch
            {
                // Ignore channel cleanup errors on shutdown
            }
        }
        _activeChannels.Clear();
        base.Dispose();
    }
}
