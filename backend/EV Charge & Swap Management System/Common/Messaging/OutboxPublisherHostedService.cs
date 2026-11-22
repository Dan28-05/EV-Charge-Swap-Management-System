using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EVChargeSwap.Common.Messaging.Consumers;
using EVChargeSwap.Data;
using EVChargeSwap.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace EVChargeSwap.Common.Messaging;

public sealed class OutboxPublisherHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<OutboxPublisherHostedService> _logger;
    private readonly TimeProvider _clock;

    private IChannel? _publisherChannel;
    private HashSet<string>? _subscribedKeys;

    public OutboxPublisherHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<RabbitMqOptions> options,
        ILogger<OutboxPublisherHostedService> logger,
        TimeProvider? clock = null)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
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

                await ResetPublisherChannelAsync();
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

        await ResetPublisherChannelAsync();
        _logger.LogInformation("Outbox publisher background service stopped.");
    }

    private async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var connectionProvider = scope.ServiceProvider.GetRequiredService<IRabbitMqConnectionProvider>();
        if (!connectionProvider.IsConfigured)
        {
            return 0;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        List<OutboxMessage> pendingMessages;
        if (db.Database.IsNpgsql())
        {
            pendingMessages = await db.OutboxMessages
                .FromSqlRaw(
                    """
                    SELECT * FROM outbox_messages
                    WHERE "ProcessedAtUtc" IS NULL AND "RetryCount" < {0}
                    ORDER BY "CreatedAtUtc"
                    FOR UPDATE SKIP LOCKED
                    LIMIT {1}
                    """,
                    _options.MaxRetryCount, _options.PublishBatchSize)
                .ToListAsync(ct);
        }
        else
        {
            pendingMessages = await db.OutboxMessages
                .Where(x => x.ProcessedAtUtc == null && x.RetryCount < _options.MaxRetryCount)
                .OrderBy(x => x.CreatedAtUtc)
                .Take(_options.PublishBatchSize)
                .ToListAsync(ct);
        }

        if (pendingMessages.Count == 0)
        {
            return 0;
        }

        var (channel, subscribedKeys) = await EnsurePublisherChannelAsync(scope.ServiceProvider, connectionProvider, ct);

        var publishedCount = 0;
        Exception? brokerFailure = null;
        foreach (var message in pendingMessages)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

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

                message.ProcessedAtUtc = _clock.GetUtcNow();
                publishedCount++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (IsBrokerTransportException(ex))
            {
                // Persist messages already published earlier in this batch, then rethrow so backoff applies
                // without burning RetryCount on remaining rows during a broker outage.
                brokerFailure = ex;
                break;
            }
            catch (Exception ex)
            {
                message.RetryCount++;
                message.LastError = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
                _logger.LogWarning(ex, "Failed to publish outbox message {Id} to RabbitMQ (attempt {Retry}/{Max}).",
                    message.Id, message.RetryCount, _options.MaxRetryCount);
            }
        }

        // Always persist already-published rows even if shutdown was requested mid-batch.
        if (publishedCount > 0 || pendingMessages.Any(m => m.RetryCount > 0))
        {
            await db.SaveChangesAsync(CancellationToken.None);
            await transaction.CommitAsync(CancellationToken.None);
        }

        if (publishedCount > 0)
        {
            _logger.LogInformation("Successfully published {Count} outbox messages to RabbitMQ exchange '{Exchange}'.",
                publishedCount, _options.ExchangeName);
        }

        if (brokerFailure is not null)
        {
            throw brokerFailure;
        }

        return publishedCount;
    }

    private async Task<(IChannel Channel, HashSet<string> SubscribedKeys)> EnsurePublisherChannelAsync(
        IServiceProvider serviceProvider,
        IRabbitMqConnectionProvider connectionProvider,
        CancellationToken ct)
    {
        if (_publisherChannel is { IsOpen: true } && _subscribedKeys is not null)
        {
            return (_publisherChannel, _subscribedKeys);
        }

        await ResetPublisherChannelAsync();
        await connectionProvider.EnsureTopologyAsync(ct);
        var connection = await connectionProvider.GetConnectionAsync(ct);
        var channel = await connection.CreateChannelAsync(new CreateChannelOptions(
            publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), ct);

        var subscribedKeys = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (var type in RabbitMqConsumersHostedService.ConsumerTypes)
            {
                var consumer = (IConsumer)serviceProvider.GetRequiredService(type);
                await RabbitMqConsumersHostedService.DeclareConsumerQueueAsync(channel, consumer, _options.ExchangeName, ct);
                subscribedKeys.UnionWith(consumer.RoutingKeys);
            }
        }
        catch
        {
            await channel.DisposeAsync();
            throw;
        }

        _publisherChannel = channel;
        _subscribedKeys = subscribedKeys;
        return (channel, subscribedKeys);
    }

    private async Task ResetPublisherChannelAsync()
    {
        var channel = _publisherChannel;
        _publisherChannel = null;
        _subscribedKeys = null;
        if (channel is not null)
        {
            try
            {
                await channel.DisposeAsync();
            }
            catch
            {
                // Ignore cleanup errors on broken channel.
            }
        }
    }

    private static bool IsBrokerTransportException(Exception ex) =>
        ex is AlreadyClosedException or BrokerUnreachableException or OperationInterruptedException
            or IOException or TimeoutException;
}
