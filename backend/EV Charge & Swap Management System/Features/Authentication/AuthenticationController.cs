using EVChargeSwap.Common.Results;
using EVChargeSwap.Features.Authentication.Requests;
using EVChargeSwap.Features.Authentication.Responses;
using EVChargeSwap.Features.Authentication.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EVChargeSwap.Features.Authentication;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public sealed class AuthenticationController(AuthenticationService service,
    VerificationService verification, Microsoft.AspNetCore.Identity.UserManager<EVChargeSwap.Features.Users.Entities.ApplicationUser> users,
    IWebHostEnvironment environment, IServiceProvider services) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var result = await service.RegisterAsync(request, cancellationToken);
        return result.Succeeded ? Accepted() : Failure(result);
    }

    [AllowAnonymous]
    [HttpPost("verification/resend")]
    public async Task<IActionResult> Resend(ResendVerificationRequest request, CancellationToken cancellationToken)
    {
        if (!VerificationService.ValidChannel(request.Channel)) return BadRequest();
        if (!verification.DeliveryAvailable) return StatusCode(StatusCodes.Status503ServiceUnavailable);
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is not null && !(request.Channel == "email" ? user.EmailConfirmed : user.PhoneNumberConfirmed))
            await verification.SendAsync(user, request.Channel, cancellationToken);
        return Accepted();
    }

    [AllowAnonymous]
    [HttpPost("verification/confirm")]
    public async Task<IActionResult> Confirm(VerifyContactRequest request, CancellationToken cancellationToken)
    {
        if (!VerificationService.ValidChannel(request.Channel)) return BadRequest();
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null || !await verification.VerifyAsync(user, request.Channel, request.Code, cancellationToken))
            return BadRequest(new ProblemDetails { Title = "Invalid or expired verification code." });
        return NoContent();
    }

    [AllowAnonymous]
    [HttpGet("development/code")]
    public IActionResult DevelopmentCode(string channel, string destination)
    {
        var remote = HttpContext.Connection.RemoteIpAddress;
        if (!environment.IsDevelopment() || (remote is not null && !System.Net.IPAddress.IsLoopback(remote)))
            return NotFound();
        var inbox = services.GetService<DevelopmentCodeInbox>();
        var code = inbox?.GetCode(channel, destination);
        return code is null ? NotFound() : Ok(new { code });
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
            OperationFailure.Unavailable => StatusCodes.Status503ServiceUnavailable,
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
