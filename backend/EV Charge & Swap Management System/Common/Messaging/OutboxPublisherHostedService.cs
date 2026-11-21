using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EVChargeSwap.Data;
using EVChargeSwap.Common.Messaging.Consumers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace EVChargeSwap.Common.Messaging;

public sealed class OutboxPublisherHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<OutboxPublisherHostedService> _logger;

    public OutboxPublisherHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<RabbitMqOptions> options,
        ILogger<OutboxPublisherHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Outbox publisher background service is disabled via configuration.");
            return;
        }

        _logger.LogInformation("Outbox publisher background service started. Polling every {Interval}s, BatchSize: {BatchSize}.",
            _options.PollIntervalSeconds, _options.PublishBatchSize);

        var backoffDelay = TimeSpan.FromSeconds(1);
        var maxBackoff = TimeSpan.FromSeconds(30);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processedCount = await ProcessBatchAsync(stoppingToken);
                if (processedCount > 0)
                {
                    // Reset backoff on successful processing
                    backoffDelay = TimeSpan.FromSeconds(1);
                }
                else
                {
                    await Task.Delay(TimeSpan.FromSeconds(_options.PollIntervalSeconds), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Transient error occurred while publishing outbox messages. Backing off for {Delay}s.",
                    backoffDelay.TotalSeconds);

                await Task.Delay(backoffDelay, stoppingToken);
                backoffDelay = TimeSpan.FromSeconds(Math.Min(backoffDelay.TotalSeconds * 2, maxBackoff.TotalSeconds));
            }
        }

        _logger.LogInformation("Outbox publisher background service stopped.");
    }

    private async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var pendingMessages = await db.OutboxMessages
            .Where(x => x.ProcessedAtUtc == null && x.RetryCount < _options.MaxRetryCount)
            .OrderBy(x => x.CreatedAtUtc)
            .Take(_options.PublishBatchSize)
            .ToListAsync(ct);

        if (pendingMessages.Count == 0)
        {
            return 0;
        }

        var connectionProvider = scope.ServiceProvider.GetRequiredService<IRabbitMqConnectionProvider>();
        if (!connectionProvider.IsConfigured)
        {
            return 0;
        }

        IChannel channel;
        try
        {
            await connectionProvider.EnsureTopologyAsync(ct);
            var connection = await connectionProvider.GetConnectionAsync(ct);
            channel = await connection.CreateChannelAsync(new CreateChannelOptions(
                publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("RabbitMQ broker is unreachable ({Message}). {Count} outbox messages remain queued in PostgreSQL.",
                ex.Message, pendingMessages.Count);
            throw; // Let ExecuteAsync apply exponential backoff
        }

        var publishedCount = 0;
        using (channel)
        {
            var subscribedKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var type in RabbitMqConsumersHostedService.ConsumerTypes)
            {
                var consumer = (IConsumer)scope.ServiceProvider.GetRequiredService(type);
                await RabbitMqConsumersHostedService.DeclareConsumerQueueAsync(channel, consumer, _options.ExchangeName, ct);
                subscribedKeys.UnionWith(consumer.RoutingKeys);
            }

            foreach (var message in pendingMessages)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var props = new BasicProperties
                    {
                        MessageId = message.Id.ToString(),
                        Type = message.EventType,
                        ContentType = "application/json",
                        DeliveryMode = DeliveryModes.Persistent,
                        Timestamp = new AmqpTimestamp(message.CreatedAtUtc.ToUnixTimeSeconds())
                    };

                    if (!string.IsNullOrEmpty(message.TraceParent))
                    {
                        props.Headers = new Dictionary<string, object?>
                        {
                            ["traceparent"] = message.TraceParent
                        };
                    }

                    var body = Encoding.UTF8.GetBytes(message.Payload);
                    await channel.BasicPublishAsync(
                        exchange: _options.ExchangeName,
                        routingKey: message.RoutingKey,
                        mandatory: subscribedKeys.Contains(message.RoutingKey),
                        basicProperties: props,
                        body: body,
                        cancellationToken: ct);

                    message.ProcessedAtUtc = DateTimeOffset.UtcNow;
                    publishedCount++;
                }
                catch (Exception ex)
                {
                    message.RetryCount++;
                    message.LastError = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
                    _logger.LogWarning(ex, "Failed to publish outbox message {Id} to RabbitMQ (attempt {Retry}/{Max}).",
                        message.Id, message.RetryCount, _options.MaxRetryCount);
                }
            }

            await db.SaveChangesAsync(ct);
        }

        if (publishedCount > 0)
        {
            _logger.LogInformation("Successfully published {Count} outbox messages to RabbitMQ exchange '{Exchange}'.",
                publishedCount, _options.ExchangeName);
        }

        return publishedCount;
    }
}
