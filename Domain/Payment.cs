namespace Gym_Management.Domain;

public class Payment
{
    public Guid Id { get; set; }

    public Guid SubscriptionId { get; set; }

    public Guid CustomerId { get; set; }

    public decimal Amount { get; set; }

    public PaymentMethod Method { get; set; }

    public string? Note { get; set; }

    public DateTime RecordedAtUtc { get; set; }

    public Guid RecordedByStaffId { get; set; }
}
