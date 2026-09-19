namespace Quotely.Api.Models;

/// <summary>
/// One payment against an invoice, from either of the two ways money arrives.
///
/// A <see cref="PaymentSource.Gateway"/> row is created when we ask the provider for an order and
/// completed when the provider tells us — via checkout verification or a webhook — what happened
/// to it. A <see cref="PaymentSource.Manual"/> row is entered by the owner after they were handed
/// cash, a cheque or a bank transfer, and exists fully formed the moment it is created.
///
/// There is one table and one ledger. Which source a row came from changes what may be done to it
/// — only a manual payment can be voided — but never how it is counted.
///
/// Nothing sensitive is stored: no card number, no CVV, no bank credential, no provider secret.
/// The method string is a coarse label ("upi", "card", "cash"), which is safe to display.
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

    /// <summary>Gateway or manual. Never inferred from <see cref="Provider"/>.</summary>
    public PaymentSource Source { get; set; } = PaymentSource.Gateway;

    /// <summary>
    /// Provider key, e.g. "Razorpay", or <see cref="PaymentProviders.Manual"/> for money received
    /// directly. Kept as a string so a second provider needs no migration.
    /// </summary>
    public string Provider { get; set; } = PaymentProviders.Razorpay;

    /// <summary>
    /// Which merchant payment connection collected this money — a reference, never a copy of the
    /// credentials. Null for a manual payment, and for gateway rows written before V2.7 when
    /// there was only one account to collect into.
    ///
    /// Recorded so a payment can be traced to the account that actually holds it, and so a
    /// webhook can be refused when it names a payment belonging to a different connection.
    /// </summary>
    public Guid? MerchantConnectionId { get; set; }

    /// <summary>
    /// The provider's order identifier. Indexed, not unique: one order may be retried.
    ///
    /// Null for a manual payment, because there genuinely is no provider order behind a handful
    /// of cash — not an empty string or a placeholder, which would make "has no order" and "has
    /// an order we failed to record" indistinguishable.
    /// </summary>
    public string? ProviderOrderId { get; set; }

    /// <summary>
    /// The provider's payment identifier, set once a real payment exists. Uniquely indexed, so
    /// the database — not just application logic — guarantees one provider payment cannot be
    /// recorded twice.
    /// </summary>
    public string? ProviderPaymentId { get; set; }

    /// <summary>
    /// "upi", "card", "netbanking" … whatever the provider reports, or one of
    /// <see cref="ManualPaymentMethods"/> for a payment the owner recorded. Never card details.
    /// </summary>
    public string? Method { get; set; }

    /// <summary>
    /// The owner's own reference for a manual payment: a cheque number, a bank UTR, a transaction
    /// note. Free text, shown back to them so they can reconcile against a bank statement. Null
    /// for gateway payments, which are identified by <see cref="ProviderPaymentId"/> instead.
    /// </summary>
    public string? Reference { get; set; }

    /// <summary>The owner's note about this payment. Never shown to the customer.</summary>
    public string? Notes { get; set; }

    /// <summary>Who the checkout was prefilled for, copied from the invoice snapshot.</summary>
    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }

    /// <summary>Provider-supplied reason, stored so the owner can see why an attempt failed.</summary>
    public string? FailureReason { get; set; }

    public DateTime? PaidAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the owner voided this payment, or null if it still stands. Voiding never deletes:
    /// financial history is kept, and the row simply stops counting toward the invoice balance.
    /// Only a <see cref="PaymentSource.Manual"/> payment may be voided.
    /// </summary>
    public DateTime? VoidedAt { get; set; }

    /// <summary>Optimistic concurrency guard for simultaneous verification and webhook processing.</summary>
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Holds this invoice's single payment slot while the attempt is live, and is null once it
    /// settles. A unique index over this column is what stops two browser tabs from each opening
    /// an order for the same outstanding balance: the second insert loses at the database, not at
    /// an application "if not exists" check that two threads can pass simultaneously.
    ///
    /// The value is the invoice id, so "one live reservation per invoice" falls straight out of
    /// the uniqueness constraint.
    /// </summary>
    public Guid? ReservationSlot { get; set; }

    public bool IsSettled => Status == PaymentStatus.Captured;

    /// <summary>An attempt still holding money, or about to: Created and Pending both reserve.</summary>
    public bool IsLiveAttempt => Status is PaymentStatus.Created or PaymentStatus.Pending;

    public bool IsVoided => VoidedAt is not null;

    /// <summary>
    /// Whether this row is money the business actually holds against the invoice. This is the
    /// definition of "paid", and it is the same one the ledger queries use — see
    /// <c>InvoiceLedger</c>, which expresses it in a form the database can evaluate.
    /// </summary>
    public bool CountsTowardPaid => Status == PaymentStatus.Captured && VoidedAt is null;

    /// <summary>
    /// Gateway money belongs to the provider's record, not ours, so it is never voided here — a
    /// mistaken gateway payment is resolved by a refund, which V2.5 does not implement.
    /// </summary>
    public bool CanBeVoided => Source == PaymentSource.Manual && VoidedAt is null;
}

public static class PaymentProviders
{
    public const string Razorpay = "Razorpay";

    /// <summary>
    /// The "provider" recorded against money the business received directly. Not a gateway — it
    /// is what fills the column for a payment that had no gateway involved at all.
    /// </summary>
    public const string Manual = "Manual";
}
