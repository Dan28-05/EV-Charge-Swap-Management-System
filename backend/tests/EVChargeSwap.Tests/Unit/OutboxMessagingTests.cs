namespace EVChargeSwap.Tests.Unit;

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EVChargeSwap.Common.Messaging;
using EVChargeSwap.Common.Messaging.Events;
using EVChargeSwap.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

public sealed class OutboxMessagingTests
{
    [Fact]
    public async Task OutboxService_EnqueueAsync_AddsOutboxMessageToDbContext()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var outboxService = new OutboxService(db, NullLogger<OutboxService>.Instance);

        var reservationEvent = new ReservationConfirmedEvent(
            ReservationId: Guid.NewGuid(),
            CustomerId: Guid.NewGuid(),
            LocationId: Guid.NewGuid(),
            Code: "RES-12345",
            ServiceType: "Charging",
            ExpiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(15),
            CreatedAtUtc: DateTimeOffset.UtcNow);

        await outboxService.EnqueueAsync(
            "ReservationConfirmed",
            EventRoutingKeys.ReservationConfirmed,
            reservationEvent,
            CancellationToken.None);

        await db.SaveChangesAsync();

        var message = await db.OutboxMessages.SingleOrDefaultAsync();
        Assert.NotNull(message);
        Assert.Equal("ReservationConfirmed", message.EventType);
        Assert.Equal(EventRoutingKeys.ReservationConfirmed, message.RoutingKey);
        Assert.Null(message.ProcessedAtUtc);
        Assert.Equal(0, message.RetryCount);
        Assert.Null(message.LastError);

        using var jsonDoc = JsonDocument.Parse(message.Payload);
        Assert.Equal("RES-12345", jsonDoc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task OutboxService_EnqueueAsync_ThrowsOnNullArguments()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);

        var outboxService = new OutboxService(db, NullLogger<OutboxService>.Instance);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            outboxService.EnqueueAsync("", "routing.key", new { Test = 1 }, CancellationToken.None));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            outboxService.EnqueueAsync("EventType", "   ", new { Test = 1 }, CancellationToken.None));

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            outboxService.EnqueueAsync<object>("EventType", "routing.key", null!, CancellationToken.None));
    }

    [Fact]
    public void RabbitMqConnectionProvider_IsConfigured_ReflectsOptions()
    {
        var disabledOptions = Options.Create(new RabbitMqOptions { Enabled = false, Host = "localhost" });
        var disabledProvider = new RabbitMqConnectionProvider(disabledOptions, NullLogger<RabbitMqConnectionProvider>.Instance);
        Assert.False(disabledProvider.IsConfigured);

        var emptyHostOptions = Options.Create(new RabbitMqOptions { Enabled = true, Host = "" });
        var emptyHostProvider = new RabbitMqConnectionProvider(emptyHostOptions, NullLogger<RabbitMqConnectionProvider>.Instance);
        Assert.False(emptyHostProvider.IsConfigured);

        var validOptions = Options.Create(new RabbitMqOptions { Enabled = true, Host = "rabbitmq" });
        var validProvider = new RabbitMqConnectionProvider(validOptions, NullLogger<RabbitMqConnectionProvider>.Instance);
        Assert.True(validProvider.IsConfigured);
    }
}
