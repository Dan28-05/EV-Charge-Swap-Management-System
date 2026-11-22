using EVChargeSwap.Data;
using EVChargeSwap.Features.Locations.Entities;
using EVChargeSwap.Features.Authorization;
using EVChargeSwap.Features.Users.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace EVChargeSwap.Tests.Integration;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public Guid ActiveLocationId { get; private set; }
    public Guid InactiveLocationId { get; private set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("RabbitMq:Enabled", "false");
        builder.UseSetting("ConnectionStrings:Redis", string.Empty);
        builder.UseSetting("ConnectionStrings:Default", "Host=localhost;Database=unused_by_tests;Username=test");
        builder.UseSetting("Jwt:Issuer", "EVChargeSwap.Tests");
        builder.UseSetting("Jwt:Audience", "EVChargeSwap.Tests.Client");
        builder.UseSetting("Jwt:SigningKey", "integration-test-signing-key-with-at-least-32-characters");
        builder.UseSetting("Jwt:AccessTokenMinutes", "15");
        builder.UseSetting("Jwt:RefreshTokenDays", "30");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddSingleton(_ =>
            {
                var connection = new SqliteConnection("Data Source=:memory:");
                connection.Open();
                return connection;
            });
            services.AddDbContext<AppDbContext>((provider, options) =>
                options.UseSqlite(provider.GetRequiredService<SqliteConnection>()));
            services.AddControllers().AddApplicationPart(typeof(TestErrorsController).Assembly);
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Tests use a disposable SQLite schema; production uses PostgreSQL migrations.
        db.Database.EnsureCreated();
        var first = new Location("A Station", "Address A", 10, 106, true, true);
        var inactive = new Location("Hidden Station", "Address C", 11, 107, true, false);
        inactive.Deactivate();
        db.Locations.AddRange(new Location("B Station", "Address B", 12, 108, false, true), inactive, first);
        db.SaveChanges();
        ActiveLocationId = first.Id;
        InactiveLocationId = inactive.Id;
        return host;
    }

    public async Task CreateAdminAsync(string email, string password)
    {
        using var scope = Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser(email, "Test Admin", DateTime.UtcNow);
        var createResult = await userManager.CreateAsync(user, password);
        if (!createResult.Succeeded)
            throw new InvalidOperationException(string.Join("; ", createResult.Errors.Select(x => x.Description)));
        var roleResult = await userManager.AddToRoleAsync(user, AppRoles.Admin);
        if (!roleResult.Succeeded)
            throw new InvalidOperationException(string.Join("; ", roleResult.Errors.Select(x => x.Description)));
    }
}
