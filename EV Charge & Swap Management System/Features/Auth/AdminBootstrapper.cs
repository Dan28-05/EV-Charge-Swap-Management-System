using Microsoft.AspNetCore.Identity;

namespace EVChargeSwap.Features.Auth;

public sealed class AdminBootstrapper(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<AdminBootstrapper> logger)
{
    public async Task InitializeAsync()
    {
        var email = configuration["BootstrapAdmin:Email"]?.Trim();
        if (string.IsNullOrWhiteSpace(email)) return;

        var password = configuration["BootstrapAdmin:Password"];
        var fullName = configuration["BootstrapAdmin:FullName"]?.Trim();
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(fullName))
            throw new InvalidOperationException(
                "BootstrapAdmin:Password and BootstrapAdmin:FullName are required when BootstrapAdmin:Email is set.");
        if (!await roleManager.RoleExistsAsync(AppRoles.Admin))
            throw new InvalidOperationException("The Admin role is missing. Apply the database migration before bootstrapping an admin.");

        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser(email, fullName, timeProvider.GetUtcNow().UtcDateTime);
            var createResult = await userManager.CreateAsync(user, password);
            if (!createResult.Succeeded)
                throw new InvalidOperationException("Admin bootstrap failed: " +
                    string.Join("; ", createResult.Errors.Select(x => x.Description)));
        }
        if (!await userManager.IsInRoleAsync(user, AppRoles.Admin))
        {
            var roleResult = await userManager.AddToRoleAsync(user, AppRoles.Admin);
            if (!roleResult.Succeeded)
                throw new InvalidOperationException("Admin role assignment failed: " +
                    string.Join("; ", roleResult.Errors.Select(x => x.Description)));
        }

        logger.LogInformation("Bootstrap admin account is ready.");
    }
}
