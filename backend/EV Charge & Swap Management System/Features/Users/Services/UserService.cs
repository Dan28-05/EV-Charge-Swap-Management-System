using System.Data;
using EVChargeSwap.Common.Results;
using EVChargeSwap.Data;
using EVChargeSwap.Features.Authentication.Services;
using EVChargeSwap.Features.Authorization;
using EVChargeSwap.Features.Users.Entities;
using EVChargeSwap.Features.Users.Requests;
using EVChargeSwap.Features.Users.Responses;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EVChargeSwap.Features.Users.Services;

public sealed class UserService(
    UserManager<ApplicationUser> userManager,
    AppDbContext db,
    TimeProvider timeProvider)
{
    public async Task<OperationResult<UserResponse>> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null || !user.IsActive)
            return OperationResult<UserResponse>.Fail(OperationFailure.NotFound, "User was not found.");

        var roles = await userManager.GetRolesAsync(user);
        return OperationResult<UserResponse>.Success(ToResponse(user, roles));
    }

    public async Task<OperationResult<UserResponse>> UpdateProfileAsync(
        Guid userId, UpdateProfileRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.FullName.Trim().Length is < 2 or > 160)
            return OperationResult<UserResponse>.Fail(OperationFailure.Validation,
                "Full name must contain 2 to 160 characters.");
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null || !user.IsActive)
            return OperationResult<UserResponse>.Fail(OperationFailure.NotFound, "User was not found.");

        user.UpdateFullName(request.FullName);
        var updated = await userManager.UpdateAsync(user);
        if (!updated.Succeeded)
            return OperationResult<UserResponse>.Fail(
                OperationFailure.Validation, "Could not update the profile.", ToErrors(updated));

        var roles = await userManager.GetRolesAsync(user);
        return OperationResult<UserResponse>.Success(ToResponse(user, roles));
    }

    public async Task<OperationResult<bool>> ChangePasswordAsync(
        Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null || !user.IsActive)
            return OperationResult<bool>.Fail(OperationFailure.NotFound, "User was not found.");
        if (await userManager.IsLockedOutAsync(user))
            return OperationResult<bool>.Fail(OperationFailure.Validation,
                "Account is temporarily locked due to too many failed attempts. Please try again later.");
        if (request.CurrentPassword == request.NewPassword)
            return OperationResult<bool>.Fail(OperationFailure.Validation, "The new password must be different.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var changed = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!changed.Succeeded)
        {
            if (changed.Errors.Any(e => string.Equals(e.Code, "PasswordMismatch", StringComparison.Ordinal)))
            {
                await userManager.AccessFailedAsync(user);
                await transaction.CommitAsync(cancellationToken);
            }
            return OperationResult<bool>.Fail(OperationFailure.Validation,
                "Current password is incorrect or the new password does not meet the requirements.", ToErrors(changed));
        }

        await userManager.ResetAccessFailedCountAsync(user);
        await RevokeSessionsAsync(userId, "password_changed", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return OperationResult<bool>.Success(true);
    }

    public async Task RevokeSessionsAsync(Guid userId, string reason, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var newStamp = Guid.NewGuid();
        await db.RefreshTokens
            .Where(x => x.UserId == userId && x.RevokedAtUtc == null && x.ExpiresAtUtc > now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.RevokedAtUtc, now)
                .SetProperty(x => x.RevokedReason, reason)
                .SetProperty(x => x.ConcurrencyStamp, newStamp), cancellationToken);
    }

    public async Task<OperationResult<UserResponse>> CreateOperatorAsync(
        CreateOperatorRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim();
        string? normalizedPhone = null;
        if (!string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            normalizedPhone = PhoneNumberNormalizer.Normalize(request.PhoneNumber);
            if (normalizedPhone is null)
                return OperationResult<UserResponse>.Fail(
                    OperationFailure.Validation, "Enter a valid Vietnamese mobile number.");
            if (await db.Users.AnyAsync(x => x.PhoneNumber == normalizedPhone, cancellationToken))
                return OperationResult<UserResponse>.Fail(
                    OperationFailure.Conflict, "An account with this phone number already exists.");
        }

        if (await userManager.FindByEmailAsync(email) is not null)
            return OperationResult<UserResponse>.Fail(
                OperationFailure.Conflict, "An account with this email already exists.");

        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        var user = new ApplicationUser(email, request.FullName,
            timeProvider.GetUtcNow().UtcDateTime)
        {
            PhoneNumber = normalizedPhone
        };

        try
        {
            var createResult = await userManager.CreateAsync(user, request.Password);
            if (!createResult.Succeeded)
                return OperationResult<UserResponse>.Fail(
                    OperationFailure.Validation, "Operator creation failed.", ToErrors(createResult));

            var roleResult = await userManager.AddToRoleAsync(user, AppRoles.Operator);
            if (!roleResult.Succeeded)
                return OperationResult<UserResponse>.Fail(
                    OperationFailure.Validation, "Could not assign the operator role.", ToErrors(roleResult));

            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return OperationResult<UserResponse>.Fail(
                OperationFailure.Conflict, "An account with this email or phone number already exists.");
        }

        return OperationResult<UserResponse>.Success(ToResponse(user, [AppRoles.Operator]));
    }

    private static UserResponse ToResponse(ApplicationUser user, IEnumerable<string> roles) =>
        new(user.Id, user.Email ?? string.Empty, user.FullName,
            user.PhoneNumber, user.EmailConfirmed, user.PhoneNumberConfirmed,
            user.IsActive, roles.ToArray());

    private static IReadOnlyDictionary<string, string[]> ToErrors(IdentityResult result) =>
        result.Errors.GroupBy(x => x.Code)
            .ToDictionary(x => x.Key, x => x.Select(error => error.Description).ToArray());
}
