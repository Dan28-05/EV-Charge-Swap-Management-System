using EVChargeSwap.Common.Results;
using EVChargeSwap.Features.Authentication.Requests;
using EVChargeSwap.Features.Authentication.Responses;
using EVChargeSwap.Features.Authentication.Services;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EVChargeSwap.Features.Authentication;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public sealed class AuthenticationController(AuthenticationService service,
    VerificationService verification, PasswordResetService passwordReset,
    Microsoft.AspNetCore.Identity.UserManager<EVChargeSwap.Features.Users.Entities.ApplicationUser> users,
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
        if (!verification.CanDeliver(request.Channel)) return StatusCode(StatusCodes.Status503ServiceUnavailable);
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is not null && !(request.Channel == "email" ? user.EmailConfirmed : user.PhoneNumberConfirmed))
        {
            try
            {
                await verification.SendAsync(user, request.Channel, cancellationToken);
            }
            catch (HttpRequestException)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    new ProblemDetails { Title = "Could not deliver the verification code. Please try again." });
            }
        }
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

    [Authorize]
    [HttpPost("verification/me/send")]
    public async Task<IActionResult> SendMyVerificationCode(
        SendMyVerificationCodeRequest request, CancellationToken cancellationToken)
    {
        if (!VerificationService.ValidChannel(request.Channel))
            return BadRequest(new ProblemDetails { Title = "Invalid verification channel." });
        if (!verification.CanDeliver(request.Channel))
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new ProblemDetails { Title = "Verification delivery is not configured." });

        var user = await GetCurrentUserAsync();
        if (user is null) return Unauthorized();
        if (request.Channel == "email" ? user.EmailConfirmed : user.PhoneNumberConfirmed)
            return NoContent();

        try
        {
            var sent = await verification.SendAsync(user, request.Channel, cancellationToken);
            if (!sent)
                return BadRequest(new ProblemDetails { Title = "No contact destination is configured for this channel." });
            return Accepted();
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new ProblemDetails { Title = "Could not deliver the verification code. Please try again." });
        }
    }

    [Authorize]
    [HttpPost("verification/me/confirm")]
    public async Task<IActionResult> ConfirmMyVerificationCode(
        ConfirmMyVerificationCodeRequest request, CancellationToken cancellationToken)
    {
        if (!VerificationService.ValidChannel(request.Channel))
            return BadRequest(new ProblemDetails { Title = "Invalid verification channel." });

        var user = await GetCurrentUserAsync();
        if (user is null) return Unauthorized();
        if (request.Channel == "email" ? user.EmailConfirmed : user.PhoneNumberConfirmed)
            return NoContent();
        if (!await verification.VerifyAsync(user, request.Channel, request.Code, cancellationToken))
            return BadRequest(new ProblemDetails { Title = "Invalid or expired verification code." });

        return NoContent();
    }

    [AllowAnonymous]
    [HttpGet("development/code")]
    public IActionResult DevelopmentCode(string channel, string destination)
    {
        var remote = HttpContext.Connection.RemoteIpAddress;
        if (!environment.IsDevelopment() || remote is null || !System.Net.IPAddress.IsLoopback(remote))
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
    [HttpPost("password/forgot")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request,
        CancellationToken cancellationToken)
    {
        if (!passwordReset.CanDeliver(request.Identifier)) return StatusCode(StatusCodes.Status503ServiceUnavailable);
        try
        {
            await passwordReset.RequestAsync(request, cancellationToken);
            return Accepted();
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new ProblemDetails { Title = "Could not deliver the reset code. Please try again." });
        }
    }

    [AllowAnonymous]
    [HttpPost("password/reset")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var result = await passwordReset.ResetAsync(request, cancellationToken);
        return result.Succeeded ? NoContent() : Failure(result);
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

    private async Task<EVChargeSwap.Features.Users.Entities.ApplicationUser?> GetCurrentUserAsync()
    {
        var subject = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        return Guid.TryParse(subject, out var userId)
            ? await users.FindByIdAsync(userId.ToString())
            : null;
    }
}
