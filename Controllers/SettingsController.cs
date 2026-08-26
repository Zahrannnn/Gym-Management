using Gym_Management.Domain;
using Gym_Management.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Gym_Management.Controllers;

/// <summary>Gym settings (Admin only): name, timezone, duplicate-scan window, low-balance threshold.</summary>
[ApiController]
[Authorize(Roles = nameof(StaffRole.Admin))]
[Tags("Settings")]
[Route("api/settings")]
public class SettingsController(ISettingsService settings) : ControllerBase
{
    /// <summary>Read all settings as a key/value map.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyDictionary<string, string>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyDictionary<string, string>>> Get(CancellationToken cancellationToken)
    {
        return Ok(await settings.GetAllAsync(cancellationToken));
    }

    /// <summary>Partial update — send only keys you want to change.</summary>
    /// <remarks>
    /// Keys: <c>GymName</c>, <c>TimezoneId</c>, <c>DuplicateScanThresholdMinutes</c>, <c>LowBalanceThreshold</c>.
    /// </remarks>
    [HttpPut]
    [ProducesResponseType(typeof(IReadOnlyDictionary<string, string>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<IReadOnlyDictionary<string, string>>> Put(
        [FromBody] Dictionary<string, string> updates,
        CancellationToken cancellationToken)
    {
        if (updates is null || updates.Count == 0)
        {
            throw ApiErrors.Validation("At least one setting is required.");
        }

        await settings.UpdateAsync(updates, cancellationToken);
        return Ok(await settings.GetAllAsync(cancellationToken));
    }
}
