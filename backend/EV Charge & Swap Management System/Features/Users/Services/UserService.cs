using System.Data;
using EVChargeSwap.Common.Results;
using EVChargeSwap.Data;
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
    public async Task<OperationResult<UserResponse>> GetAsync(Guid userId)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null || !user.IsActive)
            return OperationResult<UserResponse>.Fail(OperationFailure.NotFound, "User was not found.");

        var roles = await userManager.GetRolesAsync(user);
        return OperationResult<UserResponse>.Success(ToResponse(user, roles));
    }

    public async Task<OperationResult<UserResponse>> CreateOperatorAsync(
        CreateOperatorRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim();
        if (await userManager.FindByEmailAsync(email) is not null)
            return OperationResult<UserResponse>.Fail(
                OperationFailure.Conflict, "An account with this email already exists.");

        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        var user = new ApplicationUser(email, request.FullName,
            timeProvider.GetUtcNow().UtcDateTime)
        {
            PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber)
                ? null
                : request.PhoneNumber.Trim()
        };

        var createResult = await userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
            return OperationResult<UserResponse>.Fail(
                OperationFailure.Validation, "Operator creation failed.", ToErrors(createResult));

        var roleResult = await userManager.AddToRoleAsync(user, AppRoles.Operator);
        if (!roleResult.Succeeded)
            return OperationResult<UserResponse>.Fail(
                OperationFailure.Validation, "Could not assign the operator role.", ToErrors(roleResult));

        await transaction.CommitAsync(cancellationToken);
        return OperationResult<UserResponse>.Success(ToResponse(user, [AppRoles.Operator]));
    }

    private static UserResponse ToResponse(ApplicationUser user, IEnumerable<string> roles) =>
        new(user.Id, user.Email ?? string.Empty, user.FullName,
            user.PhoneNumber, user.IsActive, roles.ToArray());

    private static IReadOnlyDictionary<string, string[]> ToErrors(IdentityResult result) =>
        result.Errors.GroupBy(x => x.Code)
            .ToDictionary(x => x.Key, x => x.Select(error => error.Description).ToArray());
}
