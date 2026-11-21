using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using EVChargeSwap.Common.Messaging.Consumers;
using EVChargeSwap.Common.Messaging.Events;
using EVChargeSwap.Data;
using EVChargeSwap.Features.Authentication.Services;
using EVChargeSwap.Features.Authorization;
using EVChargeSwap.Features.Locations.Services;
using EVChargeSwap.Features.Reservations.Entities;
using EVChargeSwap.Features.Users.Entities;
using EVChargeSwap.Features.Users.Hubs;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EVChargeSwap.Tests.Integration;

public sealed class ReservationRealtimeApiTests
{
    [Fact]
    public async Task Anonymous_client_cannot_negotiate_private_notification_hub()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var response = await client.PostAsync("/hubs/notifications/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Two_authenticated_connections_receive_only_their_own_reservation_update()
    {
        using var factory = new ApiFactory();
        using var scope = factory.Services.CreateScope();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var ct = timeout.Token;
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var tokens = scope.ServiceProvider.GetRequiredService<JwtTokenService>();
        var first = new ApplicationUser("realtime-first@example.test", "First customer", DateTime.UtcNow);
        var second = new ApplicationUser("realtime-second@example.test", "Second customer", DateTime.UtcNow);
        var operatorUser = new ApplicationUser("realtime-operator@example.test", "Station operator", DateTime.UtcNow);
        Assert.True((await users.CreateAsync(first)).Succeeded);
        Assert.True((await users.CreateAsync(second)).Succeeded);
        Assert.True((await users.CreateAsync(operatorUser)).Succeeded);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Set<OperatorAssignment>().Add(new OperatorAssignment
            { OperatorId = operatorUser.Id, LocationId = factory.ActiveLocationId });
        await db.SaveChangesAsync(ct);

        async Task<WebSocket> Connect(ApplicationUser user, string role = AppRoles.Customer)
        {
            var token = tokens.CreateAccessToken(user, [role]).Value;
            var client = factory.Server.CreateWebSocketClient();
            var socket = await client.ConnectAsync(new Uri($"wss://localhost/hubs/notifications?access_token={token}"), ct);
            await socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e")),
                WebSocketMessageType.Text, true, ct);
            Assert.Equal("{}\u001e", await Receive(socket, ct));
            return socket;
        }

        using var firstSocket = await Connect(first);
        using var secondSocket = await Connect(second);
        using var operatorSocket = await Connect(operatorUser, AppRoles.Operator);
        var station = Guid.NewGuid();
        var notifier = new RecordingNotifier();
        var consumer = new ReservationRealtimeConsumer(
            scope.ServiceProvider.GetRequiredService<IHubContext<NotificationHub>>(), notifier);

        async Task<Guid> Publish(Guid owner)
        {
            var id = Guid.NewGuid();
            await consumer.HandleAsync("ReservationChanged", EventRoutingKeys.ReservationDetailsChanged,
                JsonSerializer.Serialize(new ReservationChangedEvent(id, owner, station), JsonSerializerOptions.Web),
                Guid.NewGuid(), ct);
            return id;
        }

        await scope.ServiceProvider.GetRequiredService<IStationNotifier>()
            .NotifyStationInventoryChangedAsync(factory.ActiveLocationId, ct);
        var firstId = await Publish(first.Id);
        var secondId = await Publish(second.Id);
        Assert.Contains(firstId.ToString(), await Receive(firstSocket, ct));
        Assert.Contains(secondId.ToString(), await Receive(secondSocket, ct));
        Assert.Contains("OperatorStationChanged", await Receive(operatorSocket, ct));
        Assert.Equal(new[] { station, station }, notifier.Stations);

        var now = DateTimeOffset.UtcNow;
        var bay = new ChargingBay { LocationId = factory.ActiveLocationId, Code = "CHECKOUT", PowerKw = 60 };
        var hold = new Reservation { LocationId = factory.ActiveLocationId, CustomerId = first.Id,
            ChargingBayId = bay.Id, Code = "CHECKOUT-1", CreatedAtUtc = now, ExpiresAtUtc = now.AddMinutes(15) };
        var invoice = new Invoice { ReservationId = hold.Id, CustomerId = first.Id, KwhRate = 3858m,
            TotalKwh = 1, EnergyAmount = 3858m, TotalAmount = 3858m, Status = InvoiceStatus.Paid, PaidAtUtc = now };
        db.AddRange(bay, hold, invoice);
        await db.SaveChangesAsync(ct);

        var invoiceConsumer = scope.ServiceProvider.GetRequiredService<InvoiceRealtimeConsumer>();
        await invoiceConsumer.HandleAsync("PaymentSucceeded", EventRoutingKeys.PaymentSucceeded,
            JsonSerializer.Serialize(new PaymentSucceededEvent(first.Id, invoice.Id), JsonSerializerOptions.Web),
            Guid.NewGuid(), ct);

        var ownerRefresh = await Receive(firstSocket, ct);
        Assert.Contains("InvoiceChanged", ownerRefresh);
        Assert.Contains(invoice.Id.ToString(), ownerRefresh);
        var operatorRefresh = await Receive(operatorSocket, ct);
        Assert.Contains("InvoiceChanged", operatorRefresh);
        Assert.Contains(invoice.Id.ToString(), operatorRefresh);
        var unrelatedRefresh = await Publish(second.Id);
        var unrelatedMessage = await Receive(secondSocket, ct);
        Assert.Contains(unrelatedRefresh.ToString(), unrelatedMessage);
        Assert.DoesNotContain("InvoiceChanged", unrelatedMessage);
    }

    private static async Task<string> Receive(WebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[4096];
        var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
        Assert.Equal(WebSocketMessageType.Text, result.MessageType);
        return Encoding.UTF8.GetString(buffer, 0, result.Count);
    }

    [Fact]
    public async Task Station_snapshot_does_not_subtract_a_parked_reservation_twice()
    {
        using var factory = new ApiFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var locations = scope.ServiceProvider.GetRequiredService<LocationService>();
        var station = factory.ActiveLocationId;
        var user = new ApplicationUser("inventory@example.test", "Inventory test", DateTime.UtcNow);
        Assert.True((await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
            .CreateAsync(user)).Succeeded);
        var first = new ChargingBay { LocationId = station, Code = "A", PowerKw = 60 };
        db.ChargingBays.AddRange(first, new ChargingBay { LocationId = station, Code = "B", PowerKw = 60 });
        await db.SaveChangesAsync();
        async Task<int?> Available() => Assert.Single(await locations.GetInventorySnapshotAsync([station], default))
            .AvailableChargingBays;
        Assert.Equal(2, await Available());
        var now = DateTimeOffset.UtcNow;
        var hold = new Reservation { LocationId = station, CustomerId = user.Id, ChargingBayId = first.Id,
            Code = Guid.NewGuid().ToString("N"), CreatedAtUtc = now, ExpiresAtUtc = now.AddMinutes(15) };
        db.Reservations.Add(hold);
        await db.SaveChangesAsync();
        Assert.Equal(1, await Available());
        var entry = db.Entry(hold);
        entry.Property(x => x.Status).CurrentValue = ReservationStatus.CheckedIn;
        entry.Property(x => x.CheckedInAtUtc).CurrentValue = now;
        entry.Property(x => x.CheckedInById).CurrentValue = user.Id;
        entry.Property(x => x.VehicleArrivedAtUtc).CurrentValue = now;
        entry.Property(x => x.VehicleArrivedById).CurrentValue = user.Id;
        db.Entry(first).Property(x => x.ParkingState).CurrentValue = ParkingState.Occupied;
        await db.SaveChangesAsync();
        Assert.Equal(1, await Available());
        entry.Property(x => x.Status).CurrentValue = ReservationStatus.Completed;
        entry.Property(x => x.VehicleDepartedAtUtc).CurrentValue = now;
        entry.Property(x => x.VehicleDepartedById).CurrentValue = user.Id;
        entry.Property(x => x.ClosedAtUtc).CurrentValue = now;
        entry.Property(x => x.ClosedById).CurrentValue = user.Id;
        db.Entry(first).Property(x => x.ParkingState).CurrentValue = ParkingState.Vacant;
        await db.SaveChangesAsync();
        Assert.Equal(2, await Available());
    }

    private sealed class RecordingNotifier : IStationNotifier
    {
        public List<Guid> Stations { get; } = [];
        public Task NotifyStationInventoryChangedAsync(Guid locationId, CancellationToken ct = default)
        {
            Stations.Add(locationId);
            return Task.CompletedTask;
        }
    }
}
