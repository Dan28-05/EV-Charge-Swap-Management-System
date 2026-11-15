using EVChargeSwap.Common.Results;
using EVChargeSwap.Features.Authentication.Requests;
using EVChargeSwap.Features.Authentication.Responses;
using EVChargeSwap.Features.Authentication.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EVChargeSwap.Features.Authentication;

[ApiController]
[Route("api/auth")]
public sealed class AuthenticationController(AuthenticationService service) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<ActionResult<AuthenticationResponse>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var result = await service.RegisterAsync(request, cancellationToken);
        return result.Succeeded ? Created("/api/users/me", result.Value) : Failure(result);
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<AuthenticationResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await service.LoginAsync(request, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : Failure(result);
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<ActionResult<AuthenticationResponse>> Refresh(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        var result = await service.RefreshAsync(request.RefreshToken, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : Failure(result);
    }

    [AllowAnonymous]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        await service.LogoutAsync(request.RefreshToken, cancellationToken);
        return NoContent();
    }

    private ObjectResult Failure<T>(OperationResult<T> result)
    {
        var status = result.Failure switch
        {
            OperationFailure.Validation => StatusCodes.Status400BadRequest,
            OperationFailure.Conflict => StatusCodes.Status409Conflict,
            OperationFailure.Unauthorized => StatusCodes.Status401Unauthorized,
            OperationFailure.NotFound => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status500InternalServerError
        };
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
