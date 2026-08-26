using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Gym_Management.Auth;
using Gym_Management.Data;
using Gym_Management.Domain;
using Gym_Management.Observability;
using Gym_Management.Services;
using Gym_Management.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Gym_Management.Controllers;

public class LoginRequest
{
    [NotBlank(ErrorMessage = "username is required and cannot be blank.")]
    [MaxLength(100, ErrorMessage = "username must be at most 100 characters.")]
    public string Username { get; set; } = string.Empty;

    [NotBlank(ErrorMessage = "password is required and cannot be blank.")]
    [MaxLength(200, ErrorMessage = "password must be at most 200 characters.")]
    public string Password { get; set; } = string.Empty;
}

public record LoginResponse(string Token, string Role, string FullName);

public record MeResponse(Guid Id, string Username, string Role, string FullName);

/// <summary>Login and “who am I”.</summary>
[ApiController]
[Tags("Auth")]
[Route("api/auth")]
public class AuthController(
    GymDbContext db,
    ITokenService tokenService,
    IPasswordHasher<StaffUser> passwordHasher,
    ILogger<AuthController> logger) : ControllerBase
{
    /// <summary>Login — get a JWT for Staff or Admin.</summary>
    /// <remarks>
    /// Default admin: <c>admin</c> / <c>Admin#12345!</c>.
    /// Then click <b>Authorize</b> and paste the returned <c>token</c>.
    /// Failures: <c>401 unauthorized</c>, <c>422 validation</c>.
    /// </remarks>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        using var activity = GymActivities.Source.StartActivity(GymActivities.Operations.Login);
        activity?.SetTag("auth.username", request.Username);

        var user = await db.StaffUsers.SingleOrDefaultAsync(u => u.Username == request.Username, cancellationToken);
        if (user is null || !user.IsActive)
        {
            logger.LogWarning(GymLogEvents.AuthLoginFailed, "Login failed for username={Username} reason=unknown_or_inactive", request.Username);
            throw ApiErrors.Unauthorized("Invalid username or password.");
        }

        var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verification == PasswordVerificationResult.Failed)
        {
            logger.LogWarning(GymLogEvents.AuthLoginFailed, "Login failed for username={Username} reason=bad_password", request.Username);
            throw ApiErrors.Unauthorized("Invalid username or password.");
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
            await db.SaveChangesAsync(cancellationToken);
        }

        var (token, _) = tokenService.CreateToken(user);
        activity?.SetTag("staff.id", user.Id.ToString());
        activity?.SetTag("staff.role", user.Role.ToString());
        logger.LogInformation(
            GymLogEvents.AuthLoginSucceeded,
            "Login succeeded staffId={StaffId} role={Role} username={Username}",
            user.Id,
            user.Role,
            user.Username);
        return Ok(new LoginResponse(token, user.Role.ToString(), user.FullName));
    }

    /// <summary>Current user from the JWT (health-check for the token).</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(MeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<MeResponse>> Me(CancellationToken cancellationToken)
    {
        var staffIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(staffIdClaim, out var staffId))
        {
            throw ApiErrors.Unauthorized();
        }

        var user = await db.StaffUsers.SingleOrDefaultAsync(u => u.Id == staffId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            throw ApiErrors.Unauthorized();
        }

        return Ok(new MeResponse(user.Id, user.Username, user.Role.ToString(), user.FullName));
    }
}
