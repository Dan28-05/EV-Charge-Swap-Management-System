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
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqConnectionProvider> _logger;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private IConnection? _connection;
    private bool _topologyDeclared;

    public RabbitMqConnectionProvider(IOptions<RabbitMqOptions> options, ILogger<RabbitMqConnectionProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured => _options.Enabled && !string.IsNullOrWhiteSpace(_options.Host);

    public async Task<IConnection> GetConnectionAsync(CancellationToken ct = default)
    {
        if (_connection is not null && _connection.IsOpen)
        {
            return _connection;
        }

        await _connectionLock.WaitAsync(ct);
        try
        {
            if (_connection is not null && _connection.IsOpen)
            {
                return _connection;
            }

            if (_connection is not null)
            {
                try
                {
                    await _connection.DisposeAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error disposing stale RabbitMQ connection.");
                }
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
            _connection = await factory.CreateConnectionAsync(ct);
            _logger.LogInformation("Connected to RabbitMQ broker successfully.");
            _topologyDeclared = false;

            return _connection;
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

        try
        {
            using var channel = await CreateChannelAsync(ct);
            await channel.ExchangeDeclareAsync(
                exchange: _options.ExchangeName,
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false,
                arguments: null,
                cancellationToken: ct);

            _topologyDeclared = true;
            _logger.LogInformation("RabbitMQ topic exchange '{Exchange}' verified.", _options.ExchangeName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to declare RabbitMQ exchange '{Exchange}'.", _options.ExchangeName);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _connectionLock.Dispose();
        if (_connection is not null)
        {
            try
            {
                await _connection.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing RabbitMQ connection on shutdown.");
            }
        }
    }
}
