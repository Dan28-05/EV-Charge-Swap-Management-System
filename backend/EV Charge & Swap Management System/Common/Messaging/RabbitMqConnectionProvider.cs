using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace EVChargeSwap.Common.Messaging;

public interface IRabbitMqConnectionProvider : IAsyncDisposable
{
    Task<IConnection> GetConnectionAsync(CancellationToken ct = default);
    Task<IChannel> CreateChannelAsync(CancellationToken ct = default);
    Task EnsureTopologyAsync(CancellationToken ct = default);
    bool IsConfigured { get; }
}

public sealed class RabbitMqConnectionProvider : IRabbitMqConnectionProvider
{
    internal const string DeadLetterExchange = "ev.dlx";
    internal const string DeadLetterQueue = "ev.dlq";

    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqConnectionProvider> _logger;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private volatile IConnection? _connection;
    private volatile bool _topologyDeclared;
    private int _disposed;

    public RabbitMqConnectionProvider(IOptions<RabbitMqOptions> options, ILogger<RabbitMqConnectionProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured => _options.Enabled && !string.IsNullOrWhiteSpace(_options.Host);

    public async Task<IConnection> GetConnectionAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        var existing = _connection;
        if (existing is not null && existing.IsOpen)
        {
            return existing;
        }

        await _connectionLock.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

            existing = _connection;
            if (existing is not null)
            {
                if (existing.IsOpen)
                {
                    return existing;
                }

                // When AutomaticRecoveryEnabled is true, RabbitMQ.Client recovers network drops in-place.
                // Only dispose and recreate if the connection was explicitly shut down by the application.
                if (existing.CloseReason is not { Initiator: ShutdownInitiator.Application })
                {
                    return existing;
                }

                try
                {
                    await existing.DisposeAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error disposing closed RabbitMQ connection.");
                }
                _connection = null;
            }

            var factory = new ConnectionFactory
            {
                HostName = _options.Host,
                Port = _options.Port,
                UserName = _options.Username,
                Password = _options.Password,
                VirtualHost = _options.VirtualHost,
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
                ClientProvidedName = "EVChargeSwap.Api"
            };

            _logger.LogInformation("Connecting to RabbitMQ broker at {Host}:{Port}...", _options.Host, _options.Port);
            var created = await factory.CreateConnectionAsync(ct);
            _connection = created;
            _topologyDeclared = false;
            _logger.LogInformation("Connected to RabbitMQ broker successfully.");

            return created;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async Task<IChannel> CreateChannelAsync(CancellationToken ct = default)
    {
        var connection = await GetConnectionAsync(ct);
        return await connection.CreateChannelAsync(cancellationToken: ct);
    }

    public async Task EnsureTopologyAsync(CancellationToken ct = default)
    {
        if (_topologyDeclared)
        {
            return;
        }

        var connection = await GetConnectionAsync(ct);

        await _connectionLock.WaitAsync(ct);
        try
        {
            if (_topologyDeclared)
            {
                return;
            }

            await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);
            await channel.ExchangeDeclareAsync(
                exchange: _options.ExchangeName,
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false,
                arguments: null,
                cancellationToken: ct);

            await channel.ExchangeDeclareAsync(
                exchange: DeadLetterExchange,
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false,
                arguments: null,
                cancellationToken: ct);

            await channel.QueueDeclareAsync(
                queue: DeadLetterQueue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null,
                cancellationToken: ct);

            await channel.QueueBindAsync(
                queue: DeadLetterQueue,
                exchange: DeadLetterExchange,
                routingKey: "#",
                arguments: null,
                cancellationToken: ct);

            _topologyDeclared = true;
            _logger.LogInformation("RabbitMQ topic exchange '{Exchange}' and DLQ topology verified.", _options.ExchangeName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to declare RabbitMQ exchange '{Exchange}'.", _options.ExchangeName);
            throw;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        var connection = _connection;
        _connection = null;
        if (connection is not null)
        {
            try
            {
                await connection.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing RabbitMQ connection on shutdown.");
            }
        }

        _connectionLock.Dispose();
    }
}
