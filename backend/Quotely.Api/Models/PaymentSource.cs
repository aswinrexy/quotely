namespace Quotely.Api.Models;

/// <summary>
/// Where a payment came from. Deliberately an explicit column rather than something inferred from
/// <see cref="Payment.Provider"/>: the rule "only a manual payment may be voided" has to be
/// unambiguous in a query and in an authorization check, and a string comparison against a
/// provider name would quietly become wrong the day a second gateway is added.
/// </summary>
public enum PaymentSource
{
    /// <summary>Collected by a payment provider — Razorpay today. Immutable once recorded.</summary>
    Gateway = 0,

    /// <summary>
    /// Money the business received directly: cash, a bank transfer, a UPI transfer to their own
    /// handle, a cheque. Entered by the owner, and therefore voidable when entered wrongly.
    /// </summary>
    Manual = 1
}

/// <summary>
/// The ways a business can receive money outside the gateway. Stored in the existing
/// <see cref="Payment.Method"/> column, which already holds the provider's own coarse label
/// ("upi", "card", "netbanking") — so manual and gateway payments describe themselves the same
/// way and the payment history needs no special case to render either.
///
/// Kept as a validated string set rather than an enum precisely because that column is shared: a
/// gateway can invent a method name at any time, and a database enum would reject it.
/// </summary>
public static class ManualPaymentMethods
{
    public const string Cash = "cash";
    public const string BankTransfer = "bank_transfer";
    public const string Upi = "upi";
    public const string Cheque = "cheque";
    public const string Other = "other";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Cash, BankTransfer, Upi, Cheque, Other
    };

    /// <summary>
    /// Returns the canonical spelling of a method the owner submitted, or null if it is not one
    /// we accept. Normalising here means the stored value is always lowercase and predictable,
    /// whatever casing the client sent.
    /// </summary>
    public static string? Normalise(string? method)
    {
        if (string.IsNullOrWhiteSpace(method)) return null;
        var trimmed = method.Trim();
        return All.Contains(trimmed) ? trimmed.ToLowerInvariant() : null;
    }

    public static string Describe() => string.Join(", ", All);
}
