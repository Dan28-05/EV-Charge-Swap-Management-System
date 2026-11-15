using System.IdentityModel.Tokens.Jwt;
using EVChargeSwap.Features.Auth.Requests;
using EVChargeSwap.Features.Auth.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EVChargeSwap.Features.Auth;

[ApiController]
[Route("api/users")]
[Authorize]
public sealed class UsersController(AuthService service) : ControllerBase
{
    [HttpGet("me")]
    public async Task<ActionResult<UserResponse>> Me()
    {
        var subject = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (!Guid.TryParse(subject, out var userId)) return Unauthorized();
        var result = await service.GetUserAsync(userId);
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

        var status = result.Failure == AuthFailure.Conflict
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
