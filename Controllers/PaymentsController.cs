using System.ComponentModel.DataAnnotations;
using Gym_Management.Data;
using Gym_Management.Domain;
using Gym_Management.Services;
using Gym_Management.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Gym_Management.Controllers;

public class CreatePaymentRequest
{
    [NotEmptyGuid(ErrorMessage = "subscriptionId must be a non-empty GUID.")]
    public Guid SubscriptionId { get; set; }

    [Range(0.01, 1_000_000, ErrorMessage = "amount must be between 0.01 and 1000000.")]
    public decimal Amount { get; set; }

    [Required(ErrorMessage = "method is required (Cash, Card, or Transfer).")]
    [EnumDataType(typeof(PaymentMethod), ErrorMessage = "method must be Cash, Card, or Transfer.")]
    public PaymentMethod Method { get; set; }

    [MaxLength(500, ErrorMessage = "note must be at most 500 characters.")]
    public string? Note { get; set; }
}

public record PaymentDto(
    Guid Id,
    Guid SubscriptionId,
    Guid CustomerId,
    decimal Amount,
    string Method,
    string? Note,
    DateTime RecordedAtUtc);

/// <summary>Offline payment recording (cash / card / transfer). Does not gate check-in.</summary>
[ApiController]
[Authorize]
[Tags("Payments")]
[Route("api/payments")]
public class PaymentsController(GymDbContext db, IGymClock clock) : ControllerBase
{
    /// <summary>Record a payment against a subscription.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(PaymentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PaymentDto>> Create(CreatePaymentRequest request, CancellationToken cancellationToken)
    {
        var staffId = StaffContext.GetStaffId(User);
        var sub = await db.Subscriptions.AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == request.SubscriptionId, cancellationToken)
            ?? throw ApiErrors.NotFound("Subscription not found.");

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            SubscriptionId = sub.Id,
            CustomerId = sub.CustomerId,
            Amount = request.Amount,
            Method = request.Method,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            RecordedAtUtc = clock.UtcNow,
            RecordedByStaffId = staffId
        };

        db.Payments.Add(payment);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new PaymentDto(
            payment.Id,
            payment.SubscriptionId,
            payment.CustomerId,
            payment.Amount,
            payment.Method.ToString(),
            payment.Note,
            payment.RecordedAtUtc));
    }
}
