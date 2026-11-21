namespace EVChargeSwap.Tests.Unit;

using System;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EVChargeSwap.Data;
using EVChargeSwap.Features.Authorization;
using EVChargeSwap.Features.Locations.Entities;
using EVChargeSwap.Features.Reservations;
using EVChargeSwap.Features.Reservations.Entities;
using EVChargeSwap.Features.Users;
using EVChargeSwap.Features.Users.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

public sealed class ControllerTests
{
    private static async Task<AppDbContext> CreateInMemoryDbContextAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private static ApplicationUser CreateTestUser(Guid id, string email)
    {
        return new ApplicationUser(email, "Test User", DateTime.UtcNow)
        {
            Id = id,
            NormalizedEmail = email.ToUpperInvariant(),
            NormalizedUserName = email.ToUpperInvariant()
        };
    }

    private static ClaimsPrincipal CreateUserPrincipal(Guid userId, string role = AppRoles.Customer)
    {
        var claims = new[]
        {
            new Claim("sub", userId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, role)
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    [Fact]
    public async Task NotificationsController_GetNotifications_ReturnsUserNotificationsAndUnreadCount()
    {
        await using var db = await CreateInMemoryDbContextAsync();
        var controller = new NotificationsController(db);
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();

        db.Users.AddRange(
            CreateTestUser(userId, "user1@example.com"),
            CreateTestUser(otherUserId, "other@example.com"));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = CreateUserPrincipal(userId) }
        };

        // Add 2 notifications: 1 read, 1 unread
        db.Notifications.AddRange(
            new Notification
            {
                UserId = userId,
                Title = "Test 1",
                Message = "Read notification",
                IsRead = true,
                CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
            },
            new Notification
            {
                UserId = userId,
                Title = "Test 2",
                Message = "Unread notification",
                IsRead = false,
                CreatedAtUtc = DateTimeOffset.UtcNow
            },
            new Notification
            {
                UserId = otherUserId,
                Title = "Other",
                Message = "Should not see",
                IsRead = false
            });
        await db.SaveChangesAsync();

        var result = await controller.GetNotifications(CancellationToken.None);
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<NotificationsListResponse>(okResult.Value);

        Assert.Equal(2, response.Items.Count);
        Assert.Equal(1, response.UnreadCount);
        Assert.Equal("Test 2", response.Items[0].Title);
    }

    [Fact]
    public async Task NotificationsController_MarkAsRead_UpdatesIsRead()
    {
        await using var db = await CreateInMemoryDbContextAsync();
        var controller = new NotificationsController(db);
        var userId = Guid.NewGuid();
        db.Users.Add(CreateTestUser(userId, "reader@example.com"));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = CreateUserPrincipal(userId) }
        };

        var notification = new Notification
        {
            UserId = userId,
            Title = "To Read",
            Message = "Content",
            IsRead = false
        };
        db.Notifications.Add(notification);
        await db.SaveChangesAsync();

        var result = await controller.MarkAsRead(notification.Id, CancellationToken.None);
        Assert.IsType<NoContentResult>(result);

        var updated = await db.Notifications.FindAsync(notification.Id);
        Assert.NotNull(updated);
        Assert.True(updated.IsRead);
        Assert.NotNull(updated.ReadAtUtc);
    }

    [Fact]
    public async Task InvoicesController_Returns_Pending_Invoice_Details()
    {
        await using var db = await CreateInMemoryDbContextAsync();
        var controller = new InvoicesController(db);
        var customerId = Guid.NewGuid();
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = CreateUserPrincipal(customerId) }
        };

        var location = new Location("Station Pay", "Address Pay", 10.5m, 106.5m, true, true);
        var user = CreateTestUser(customerId, "payer@example.com");
        db.Locations.Add(location);
        db.Users.Add(user);

        var bay = new ChargingBay
        {
            LocationId = location.Id,
            Code = "BAY-PAY",
            PowerKw = 60
        };
        db.ChargingBays.Add(bay);

        var reservation = new Reservation();
        var resId = Guid.NewGuid();
        db.Entry(reservation).Property(nameof(Reservation.Id)).CurrentValue = resId;
        db.Entry(reservation).Property(nameof(Reservation.LocationId)).CurrentValue = location.Id;
        db.Entry(reservation).Property(nameof(Reservation.CustomerId)).CurrentValue = customerId;
        db.Entry(reservation).Property(nameof(Reservation.ChargingBayId)).CurrentValue = bay.Id;
        db.Entry(reservation).Property(nameof(Reservation.Code)).CurrentValue = "RES-PAY-123";
        db.Entry(reservation).Property(nameof(Reservation.CreatedAtUtc)).CurrentValue = DateTimeOffset.UtcNow;
        db.Entry(reservation).Property(nameof(Reservation.ExpiresAtUtc)).CurrentValue = DateTimeOffset.UtcNow.AddMinutes(15);
        db.Reservations.Add(reservation);

        var invoice = new Invoice
        {
            ReservationId = resId,
            CustomerId = customerId,
            TotalKwh = 15.0m,
            KwhRate = 3858m,
            EnergyAmount = 15.0m * 3858m,
            TotalAmount = 15.0m * 3858m,
            Status = InvoiceStatus.Pending,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        // 1. Get Invoices
        var getResult = await controller.GetInvoices(CancellationToken.None);
        var okList = Assert.IsType<OkObjectResult>(getResult.Result);
        var invoices = Assert.IsAssignableFrom<IReadOnlyList<InvoiceDetailResponse>>(okList.Value);
        Assert.Single(invoices);
        Assert.Equal("RES-PAY-123", invoices[0].ReservationCode);
        Assert.Equal("Pending", invoices[0].Status);
        Assert.Equal(invoices[0].EnergyAmount, invoices[0].TotalAmount);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(invoices[0], JsonSerializerOptions.Web));
        Assert.False(json.RootElement.TryGetProperty("idleMinutes", out _));
        Assert.False(json.RootElement.TryGetProperty("idleFeeRate", out _));
        Assert.False(json.RootElement.TryGetProperty("idleAmount", out _));

        // 2. Pay Invoice
        var detail = await controller.GetInvoice(invoice.Id, CancellationToken.None);
        var current = Assert.IsType<InvoiceDetailResponse>(Assert.IsType<OkObjectResult>(detail.Result).Value);
        Assert.Equal("Pending", current.Status);
        Assert.Null(current.PaymentReference);
    }
}
