using EVChargeSwap.Common.Results;
using EVChargeSwap.Data;
using EVChargeSwap.Features.Authentication.Requests;
using EVChargeSwap.Features.Users.Entities;
using EVChargeSwap.Features.Users.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EVChargeSwap.Features.Authentication.Services;

public sealed class PasswordResetService(
    UserManager<ApplicationUser> users,
    AppDbContext db,
    VerificationService verification,
    UserService userService)
{
    public bool DeliveryAvailable => verification.DeliveryAvailable;
    public bool CanDeliver(string identifier) =>
        verification.CanDeliver(identifier.Trim().Contains('@') ? "email" : "phone");

    public async Task RequestAsync(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        var (user, destination, isEmail) = await FindUserAsync(request.Identifier, cancellationToken);
        if (user is null || !user.IsActive || !(isEmail ? user.EmailConfirmed : user.PhoneNumberConfirmed))
            return;

        await verification.SendPasswordResetAsync(user, destination, cancellationToken);
    }

    public async Task<OperationResult<bool>> ResetAsync(ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var (user, destination, isEmail) = await FindUserAsync(request.Identifier, cancellationToken);
        if (user is null || !user.IsActive || !(isEmail ? user.EmailConfirmed : user.PhoneNumberConfirmed))
            return InvalidCode();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await verification.VerifyPasswordResetAsync(user, destination, request.Code, cancellationToken))
        {
            // Persist failed attempts; successful verification and password change are committed together.
            await transaction.CommitAsync(cancellationToken);
            return InvalidCode();
        }

        var token = await users.GeneratePasswordResetTokenAsync(user);
        var changed = await users.ResetPasswordAsync(user, token, request.NewPassword);
        if (!changed.Succeeded)
            return OperationResult<bool>.Fail(OperationFailure.Validation,
                "The new password does not meet the requirements.",
                changed.Errors.GroupBy(x => x.Code)
                    .ToDictionary(x => x.Key, x => x.Select(error => error.Description).ToArray()));

        await userService.RevokeSessionsAsync(user.Id, "password_reset", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return OperationResult<bool>.Success(true);
    }

    private async Task<(ApplicationUser? User, string Destination, bool IsEmail)> FindUserAsync(
        string identifier, CancellationToken cancellationToken)
    {
        identifier = identifier.Trim();
        if (identifier.Contains('@'))
        {
            var user = await users.FindByEmailAsync(identifier);
            return (user, user?.Email ?? identifier, true);
        }

        var phone = PhoneNumberNormalizer.Normalize(identifier);
        if (phone is null) return (null, string.Empty, false);
        return (await db.Users.SingleOrDefaultAsync(x => x.PhoneNumber == phone, cancellationToken), phone, false);
    }

    private static OperationResult<bool> InvalidCode() =>
        OperationResult<bool>.Fail(OperationFailure.Validation, "Invalid or expired verification code.");
}
