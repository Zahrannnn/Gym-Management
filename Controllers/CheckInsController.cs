using System.ComponentModel.DataAnnotations;
using Gym_Management.Services;
using Gym_Management.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Gym_Management.Controllers;

public class CheckInRequest
{
    [NotBlank(ErrorMessage = "token is required and cannot be blank.")]
    [MaxLength(128, ErrorMessage = "token must be at most 128 characters.")]
    public string Token { get; set; } = string.Empty;
}

/// <summary>Reception desk QR scan — grant or deny with a reason.</summary>
[ApiController]
[Authorize]
[Tags("Check-ins")]
[Route("api/checkins")]
public class CheckInsController(ICheckInService checkIns) : ControllerBase
{
    /// <summary>Scan a customer QR token.</summary>
    /// <remarks>
    /// Always returns <b>HTTP 200</b> for domain outcomes.
    /// Success: <c>{ "result": "granted", ... }</c>.
    /// Denial: <c>{ "result": "denied", "reason": "duplicate_scan" | "expired" | ... }</c>.
    /// Auth failures still use 401/403.
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(typeof(CheckInResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CheckInResultDto>> CheckIn(CheckInRequest request, CancellationToken cancellationToken)
    {
        var staffId = StaffContext.GetStaffId(User);
        var result = await checkIns.CheckInAsync(request.Token, staffId, cancellationToken);
        return Ok(result);
    }
}
