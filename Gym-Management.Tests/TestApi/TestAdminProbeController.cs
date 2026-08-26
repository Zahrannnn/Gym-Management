using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Gym_Management.Tests.TestApi;

/// <summary>
/// Test-only endpoint (registered via ConfigureTestServices in GymApiFactory) used to
/// prove that a Staff token is rejected with 403 on an admin-only route. Never part of
/// the shipped API surface.
/// </summary>
[ApiController]
[Authorize(Roles = "Admin")]
public class TestAdminProbeController : ControllerBase
{
    [HttpGet("api/test/admin-only")]
    public IActionResult Get() => Ok(new { ok = true });
}
