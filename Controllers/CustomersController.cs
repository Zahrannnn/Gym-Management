using System.ComponentModel.DataAnnotations;
using Gym_Management.Data;
using Gym_Management.Domain;
using Gym_Management.Services;
using Gym_Management.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Gym_Management.Controllers;

public class CreateCustomerRequest
{
    [NotBlank(ErrorMessage = "firstName is required and cannot be blank.")]
    [MaxLength(100, ErrorMessage = "firstName must be at most 100 characters.")]
    public string FirstName { get; set; } = string.Empty;

    [NotBlank(ErrorMessage = "lastName is required and cannot be blank.")]
    [MaxLength(100, ErrorMessage = "lastName must be at most 100 characters.")]
    public string LastName { get; set; } = string.Empty;

    [NotBlank(ErrorMessage = "phone is required and cannot be blank.")]
    [PhoneNumber(ErrorMessage = "phone must be a valid phone number (5–30 characters; digits and + - ( ) spaces).")]
    public string Phone { get; set; } = string.Empty;

    [MaxLength(2000, ErrorMessage = "notes must be at most 2000 characters.")]
    public string? Notes { get; set; }
}

public class UpdateCustomerRequest
{
    [NotBlank(ErrorMessage = "firstName is required and cannot be blank.")]
    [MaxLength(100, ErrorMessage = "firstName must be at most 100 characters.")]
    public string FirstName { get; set; } = string.Empty;

    [NotBlank(ErrorMessage = "lastName is required and cannot be blank.")]
    [MaxLength(100, ErrorMessage = "lastName must be at most 100 characters.")]
    public string LastName { get; set; } = string.Empty;

    [NotBlank(ErrorMessage = "phone is required and cannot be blank.")]
    [PhoneNumber(ErrorMessage = "phone must be a valid phone number (5–30 characters; digits and + - ( ) spaces).")]
    public string Phone { get; set; } = string.Empty;

    [MaxLength(2000, ErrorMessage = "notes must be at most 2000 characters.")]
    public string? Notes { get; set; }
}

public record CustomerSummaryDto(
    Guid Id,
    string FirstName,
    string LastName,
    string Phone,
    bool IsActive,
    string Token,
    DateTime CreatedAtUtc);

public record CustomerCreatedDto(
    Guid Id,
    string FirstName,
    string LastName,
    string Phone,
    bool IsActive,
    string Token,
    DateTime CreatedAtUtc);

/// <summary>Card print payload. Returns stored token without rotating it.</summary>
public record CustomerCardDto(string Token, string CustomerName, string GymName);

public record PagedCustomersDto(IReadOnlyList<CustomerSummaryDto> Items, int Page, int PageSize, int Total);

public record SubscriptionDetailDto(
    Guid Id,
    string Type,
    string Status,
    DateOnly StartDate,
    DateOnly? EndDate,
    int? TotalSessions,
    int? UsedSessions,
    int? RemainingSessions,
    decimal? PricePaid,
    DateTime CreatedAtUtc);

public record AttendanceDetailDto(
    Guid Id,
    DateTime AtUtc,
    string Result,
    string? Reason,
    int? RemainingSessionsAfter);

public record PaymentDetailDto(
    Guid Id,
    Guid SubscriptionId,
    decimal Amount,
    string Method,
    string? Note,
    DateTime RecordedAtUtc);

public record CustomerDetailDto(
    Guid Id,
    string FirstName,
    string LastName,
    string Phone,
    bool IsActive,
    string? Notes,
    string Token,
    DateTime CreatedAtUtc,
    IReadOnlyList<SubscriptionDetailDto> Subscriptions,
    IReadOnlyList<AttendanceDetailDto> RecentAttendance,
    IReadOnlyList<PaymentDetailDto> Payments);

/// <summary>Members, QR tokens, archive, and card print.</summary>
[ApiController]
[Authorize]
[Tags("Customers")]
[Route("api/customers")]
public class CustomersController(
    GymDbContext db,
    IQrTokenService qrTokens,
    ISettingsService settings,
    IGymClock clock,
    IAuditService audit) : ControllerBase
{
    /// <summary>Search / list customers (includes QR <c>token</c> for reprint).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedCustomersDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedCustomersDto>> List(
        [FromQuery] string? query,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (page < 1)
        {
            throw ApiErrors.Validation("page must be >= 1.");
        }

        if (pageSize is < 1 or > 100)
        {
            throw ApiErrors.Validation("pageSize must be between 1 and 100.");
        }

        var q = db.Customers.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            q = q.Where(c =>
                c.FirstName.Contains(term) ||
                c.LastName.Contains(term) ||
                c.Phone.Contains(term) ||
                (c.FirstName + " " + c.LastName).Contains(term));
        }

        var total = await q.CountAsync(cancellationToken);
        var items = await q
            .OrderBy(c => c.LastName).ThenBy(c => c.FirstName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new CustomerSummaryDto(c.Id, c.FirstName, c.LastName, c.Phone, c.IsActive, c.Token, c.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return Ok(new PagedCustomersDto(items, page, pageSize, total));
    }

    /// <summary>Create a customer and issue a QR token.</summary>
    /// <remarks>Response includes <c>token</c> — encode that in the QR. Token is also available later on list/detail/card until reset.</remarks>
    [HttpPost]
    [ProducesResponseType(typeof(CustomerCreatedDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CustomerCreatedDto>> Create(
        CreateCustomerRequest request,
        CancellationToken cancellationToken)
    {
        var staffId = StaffContext.GetStaffId(User);
        var rawToken = qrTokens.GenerateRawToken();

        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Phone = request.Phone.Trim(),
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            Token = rawToken,
            TokenHash = qrTokens.HashToken(rawToken),
            IsActive = true,
            CreatedAtUtc = clock.UtcNow,
            CreatedByStaffId = staffId
        };

        db.Customers.Add(customer);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new CustomerCreatedDto(
            customer.Id,
            customer.FirstName,
            customer.LastName,
            customer.Phone,
            customer.IsActive,
            customer.Token,
            customer.CreatedAtUtc));
    }

    /// <summary>Customer profile + subscriptions, recent attendance, payments, and QR <c>token</c>.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(CustomerDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerDetailDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var customer = await db.Customers.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw ApiErrors.NotFound("Customer not found.");

        var today = await clock.TodayAsync(cancellationToken);
        var subscriptions = await db.Subscriptions.AsNoTracking()
            .Where(s => s.CustomerId == id)
            .OrderByDescending(s => s.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var attendance = await db.AttendanceLogs.AsNoTracking()
            .Where(a => a.CustomerId == id)
            .OrderByDescending(a => a.AtUtc)
            .Take(50)
            .ToListAsync(cancellationToken);

        var payments = await db.Payments.AsNoTracking()
            .Where(p => p.CustomerId == id)
            .OrderByDescending(p => p.RecordedAtUtc)
            .Take(50)
            .ToListAsync(cancellationToken);

        return Ok(new CustomerDetailDto(
            customer.Id,
            customer.FirstName,
            customer.LastName,
            customer.Phone,
            customer.IsActive,
            customer.Notes,
            customer.Token,
            customer.CreatedAtUtc,
            subscriptions.Select(s => ToSubDto(s, today)).ToList(),
            attendance.Select(a => new AttendanceDetailDto(
                a.Id, a.AtUtc, a.Result.ToString(), a.Reason, a.RemainingSessionsAfter)).ToList(),
            payments.Select(p => new PaymentDetailDto(
                p.Id, p.SubscriptionId, p.Amount, p.Method.ToString(), p.Note, p.RecordedAtUtc)).ToList()));
    }

    /// <summary>Update name / phone / notes (does not touch the QR token).</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(CustomerSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CustomerSummaryDto>> Update(
        Guid id,
        UpdateCustomerRequest request,
        CancellationToken cancellationToken)
    {
        var customer = await db.Customers.SingleOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw ApiErrors.NotFound("Customer not found.");

        customer.FirstName = request.FirstName.Trim();
        customer.LastName = request.LastName.Trim();
        customer.Phone = request.Phone.Trim();
        customer.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new CustomerSummaryDto(
            customer.Id, customer.FirstName, customer.LastName, customer.Phone, customer.IsActive, customer.Token, customer.CreatedAtUtc));
    }

    /// <summary>Soft-archive — check-ins will deny with <c>customer_inactive</c>.</summary>
    [HttpPost("{id:guid}/archive")]
    [ProducesResponseType(typeof(CustomerSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerSummaryDto>> Archive(Guid id, CancellationToken cancellationToken)
    {
        var customer = await db.Customers.SingleOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw ApiErrors.NotFound("Customer not found.");

        customer.IsActive = false;
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new CustomerSummaryDto(
            customer.Id, customer.FirstName, customer.LastName, customer.Phone, customer.IsActive, customer.Token, customer.CreatedAtUtc));
    }

    /// <summary>Rotate QR token — old card stops working immediately.</summary>
    /// <remarks>Only this endpoint rotates. List/detail/card keep returning the current token without changing it.</remarks>
    [HttpPost("{id:guid}/token/reset")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<object>> ResetToken(Guid id, CancellationToken cancellationToken)
    {
        var staffId = StaffContext.GetStaffId(User);
        var customer = await db.Customers.SingleOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw ApiErrors.NotFound("Customer not found.");

        var rawToken = qrTokens.GenerateRawToken();
        customer.Token = rawToken;
        customer.TokenHash = qrTokens.HashToken(rawToken);
        customer.TokenRotatedAtUtc = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await audit.WriteAsync(staffId, "TokenReset", "Customer", customer.Id.ToString(), cancellationToken: cancellationToken);

        return Ok(new { token = rawToken });
    }

    /// <summary>Card print payload: <c>token</c> + name + gym (does <b>not</b> rotate).</summary>
    [HttpGet("{id:guid}/card")]
    [ProducesResponseType(typeof(CustomerCardDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerCardDto>> Card(Guid id, CancellationToken cancellationToken)
    {
        var customer = await db.Customers.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw ApiErrors.NotFound("Customer not found.");

        if (string.IsNullOrEmpty(customer.Token))
        {
            throw ApiErrors.Validation("Customer has no printable token. Call token reset to issue one.");
        }

        var gymName = await settings.GetGymNameAsync(cancellationToken);
        var customerName = $"{customer.FirstName} {customer.LastName}".Trim();
        return Ok(new CustomerCardDto(customer.Token, customerName, gymName));
    }

    private static SubscriptionDetailDto ToSubDto(Subscription s, DateOnly today)
    {
        var status = SubscriptionStatus.Derive(s, today);
        int? remaining = s.Type == SubscriptionType.Session
            ? Math.Max(0, (s.TotalSessions ?? 0) - s.UsedSessions)
            : null;
        return new SubscriptionDetailDto(
            s.Id,
            s.Type.ToString(),
            SubscriptionStatus.ToApiString(status),
            s.StartDate,
            s.EndDate,
            s.TotalSessions,
            s.Type == SubscriptionType.Session ? s.UsedSessions : null,
            remaining,
            s.PricePaid,
            s.CreatedAtUtc);
    }
}
