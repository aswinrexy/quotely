namespace Quotely.Api.Models;

/// <summary>
/// One payment attempt against an invoice. A row is created when we ask the provider for an
/// order, then completed when the provider tells us — via checkout verification or a webhook —
/// what happened to it.
///
/// Nothing sensitive is stored: no card number, no CVV, no provider credential. The method
/// string is the coarse label the provider returns ("upi", "card"), which is safe to display.
/// </summary>
public class Payment
{
    public Guid Id { get; set; }

    public Guid InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }

    /// <summary>Denormalised owner id so payment queries stay tenant-scoped without a join.</summary>
    public Guid UserId { get; set; }

    /// <summary>The amount we asked the provider to collect, in the invoice's currency.</summary>
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "INR";

    public PaymentStatus Status { get; set; } = PaymentStatus.Created;

    /// <summary>Provider key, e.g. "Razorpay". Kept as a string so a second provider needs no migration.</summary>
    public string Provider { get; set; } = PaymentProviders.Razorpay;

    /// <summary>The provider's order identifier. Indexed, not unique: one order may be retried.</summary>
    public string ProviderOrderId { get; set; } = string.Empty;

    /// <summary>
    /// The provider's payment identifier, set once a real payment exists. Uniquely indexed, so
    /// the database — not just application logic — guarantees one provider payment cannot be
    /// recorded twice.
    /// </summary>
    public string? ProviderPaymentId { get; set; }

    /// <summary>"upi", "card", "netbanking" … whatever the provider reports. Never card details.</summary>
    public string? Method { get; set; }

    /// <summary>Who the checkout was prefilled for, copied from the invoice snapshot.</summary>
    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }

    /// <summary>Provider-supplied reason, stored so the owner can see why an attempt failed.</summary>
    public string? FailureReason { get; set; }

    public DateTime? PaidAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Optimistic concurrency guard for simultaneous verification and webhook processing.</summary>
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public bool IsSettled => Status == PaymentStatus.Captured;
}

public static class PaymentProviders
{
    public const string Razorpay = "Razorpay";
}
