using System.ComponentModel.DataAnnotations;
using Gym_Management.Data;
using Gym_Management.Domain;
using Gym_Management.Services;
using Gym_Management.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Gym_Management.Controllers;

public class CreatePlanRequest
{
    [NotBlank(ErrorMessage = "name is required and cannot be blank.")]
    [MaxLength(200, ErrorMessage = "name must be at most 200 characters.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "type is required (Time or Session).")]
    [EnumDataType(typeof(PlanType), ErrorMessage = "type must be Time or Session.")]
    public PlanType Type { get; set; }

    [Range(1, 3650, ErrorMessage = "durationDays must be between 1 and 3650 when provided.")]
    public int? DurationDays { get; set; }

    /// <summary>Ignored for Session plans — session count is chosen per subscription.</summary>
    [Range(1, 10000, ErrorMessage = "sessions must be between 1 and 10000 when provided.")]
    public int? Sessions { get; set; }

    [Range(0, 1_000_000, ErrorMessage = "price must be between 0 and 1000000.")]
    public decimal Price { get; set; }
}

public class UpdatePlanRequest
{
    [NotBlank(ErrorMessage = "name is required and cannot be blank.")]
    [MaxLength(200, ErrorMessage = "name must be at most 200 characters.")]
    public string Name { get; set; } = string.Empty;

    [Range(1, 3650, ErrorMessage = "durationDays must be between 1 and 3650 when provided.")]
    public int? DurationDays { get; set; }

    /// <summary>Ignored for Session plans — session count is chosen per subscription.</summary>
    [Range(1, 10000, ErrorMessage = "sessions must be between 1 and 10000 when provided.")]
    public int? Sessions { get; set; }

    [Range(0, 1_000_000, ErrorMessage = "price must be between 0 and 1000000.")]
    public decimal Price { get; set; }

    public bool IsActive { get; set; } = true;
}

public record PlanDto(
    Guid Id,
    string Name,
    string Type,
    int? DurationDays,
    int? Sessions,
    decimal Price,
    bool IsActive);

/// <summary>Membership plans (Admin creates/updates; Staff can list).</summary>
[ApiController]
[Authorize]
[Tags("Plans")]
[Route("api/plans")]
public class PlansController(GymDbContext db) : ControllerBase
{
    /// <summary>List all plans.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<PlanDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PlanDto>>> List(CancellationToken cancellationToken)
    {
        var plans = await db.Plans.AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new PlanDto(p.Id, p.Name, p.Type.ToString(), p.DurationDays, p.Sessions, p.Price, p.IsActive))
            .ToListAsync(cancellationToken);
        return Ok(plans);
    }

    /// <summary>
    /// Create a plan (Admin).
    /// <c>Time</c> needs <c>durationDays</c>.
    /// <c>Session</c> is a generic credit product — do <b>not</b> set <c>sessions</c>; pick the count when creating a subscription.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = nameof(StaffRole.Admin))]
    [ProducesResponseType(typeof(PlanDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PlanDto>> Create(CreatePlanRequest request, CancellationToken cancellationToken)
    {
        ValidatePlanShape(request.Type, request.DurationDays, request.Sessions);

        var plan = new Plan
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Type = request.Type,
            DurationDays = request.Type == PlanType.Time ? request.DurationDays : null,
            Sessions = null, // Session count is per subscription, not on the plan
            Price = request.Price,
            IsActive = true
        };

        db.Plans.Add(plan);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(ToDto(plan));
    }

    /// <summary>Update / deactivate a plan (Admin). Set <c>isActive: false</c> to retire it. Session plans never store a fixed session count.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = nameof(StaffRole.Admin))]
    [ProducesResponseType(typeof(PlanDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PlanDto>> Update(Guid id, UpdatePlanRequest request, CancellationToken cancellationToken)
    {
        var plan = await db.Plans.SingleOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw ApiErrors.NotFound("Plan not found.");

        ValidatePlanShape(plan.Type, request.DurationDays, request.Sessions);

        plan.Name = request.Name.Trim();
        plan.DurationDays = plan.Type == PlanType.Time ? request.DurationDays : null;
        plan.Sessions = null;
        plan.Price = request.Price;
        plan.IsActive = request.IsActive;
        await db.SaveChangesAsync(cancellationToken);

        return Ok(ToDto(plan));
    }

    private static void ValidatePlanShape(PlanType type, int? durationDays, int? sessions)
    {
        if (type == PlanType.Time)
        {
            if (durationDays is null or < 1)
            {
                throw ApiErrors.Validation("Time plans require durationDays >= 1.");
            }
        }
        else if (type == PlanType.Session)
        {
            if (sessions is not null)
            {
                throw ApiErrors.Validation(
                    "Session plans do not use a fixed sessions count. Omit sessions on the plan and pass totalSessions when creating the subscription.");
            }

            if (durationDays is not null)
            {
                throw ApiErrors.Validation("Session plans must not have durationDays.");
            }
        }
        else
        {
            throw ApiErrors.Validation("type must be Time or Session.");
        }
    }

    private static PlanDto ToDto(Plan p) =>
        new(p.Id, p.Name, p.Type.ToString(), p.DurationDays, p.Sessions, p.Price, p.IsActive);
}
