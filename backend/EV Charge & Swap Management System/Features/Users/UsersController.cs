using System.IdentityModel.Tokens.Jwt;
using EVChargeSwap.Common.Results;
using EVChargeSwap.Features.Authorization;
using EVChargeSwap.Features.Users.Requests;
using EVChargeSwap.Features.Users.Responses;
using EVChargeSwap.Features.Users.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EVChargeSwap.Features.Users;

[ApiController]
[Route("api/users")]
[Authorize]
public sealed class UsersController(UserService service) : ControllerBase
{
    [HttpGet("me")]
    public async Task<ActionResult<UserResponse>> Me()
    {
        var subject = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (!Guid.TryParse(subject, out var userId)) return Unauthorized();
        var result = await service.GetAsync(userId);
        return result.Succeeded ? Ok(result.Value) : NotFound();
    }

    [HttpPost("operators")]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<ActionResult<UserResponse>> CreateOperator(
        CreateOperatorRequest request, CancellationToken cancellationToken)
    {
        var result = await service.CreateOperatorAsync(request, cancellationToken);
        if (result.Succeeded)
            return StatusCode(StatusCodes.Status201Created, result.Value);

        var status = result.Failure == OperationFailure.Conflict
            ? StatusCodes.Status409Conflict
            : StatusCodes.Status400BadRequest;
        var problem = new ProblemDetails
        {
            Status = status,
            Title = result.Failure.ToString(),
            Detail = result.Detail,
            Instance = HttpContext.Request.Path
        };
        if (result.Errors.Count > 0) problem.Extensions["errors"] = result.Errors;
        return StatusCode(status, problem);
    }
}
