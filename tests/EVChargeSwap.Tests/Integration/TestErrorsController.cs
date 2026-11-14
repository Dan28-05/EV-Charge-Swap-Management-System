using Microsoft.AspNetCore.Mvc;

namespace EVChargeSwap.Tests.Integration;

// Loaded only by ApiFactory. This controller is never part of the production API.
[ApiController]
[Route("test-only/errors")]
public sealed class TestErrorsController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => throw new InvalidOperationException("test-only-sensitive-detail");
}
