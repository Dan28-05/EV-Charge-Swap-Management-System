using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace EVChargeSwap.Data;

// The EF CLI needs to build the model without starting the web app or connecting to a database.
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<AppDbContextFactory>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        // Model generation does not open a connection. The local fallback has no password;
        // database update uses the connection string supplied by user-secrets or the environment.
        var connectionString = configuration.GetConnectionString("Default")
            ?? "Host=localhost;Database=ev_charge_swap;Username=ev_user";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AppDbContext(options);
    }
}
