namespace Quotely.Api.Models;

/// <summary>
/// Our own payment lifecycle, deliberately independent of any provider's vocabulary.
/// Razorpay's status strings are translated into these inside the Razorpay adapter and never
/// leak into the invoice domain.
///
/// Only <see cref="Captured"/> is money we actually hold: it is the single state that counts
/// towards an invoice's paid amount.
/// </summary>
public enum PaymentStatus
{
    /// <summary>An order exists at the provider; the customer has not paid yet.</summary>
    Created = 0,

    /// <summary>Authorised but not yet captured — the money is not ours until capture.</summary>
    Pending = 1,

    /// <summary>Settled. The only state that contributes to the invoice's paid total.</summary>
    Captured = 2,

    Failed = 3,

    /// <summary>The attempt was abandoned or the order expired without payment.</summary>
    Cancelled = 4
}

public static class PaymentStatusExtensions
{
    /// <summary>Ranked so an out-of-order webhook can never walk a payment backwards.</summary>
    public static int Rank(this PaymentStatus status) => status switch
    {
        PaymentStatus.Created => 0,
        PaymentStatus.Cancelled => 1,
        PaymentStatus.Failed => 2,
        PaymentStatus.Pending => 3,
        PaymentStatus.Captured => 4,
        _ => 0
    };

    /// <summary>
    /// True when <paramref name="incoming"/> is at least as authoritative as what we hold.
    /// Razorpay does not guarantee webhook ordering, so payment.authorized arriving after
    /// payment.captured must not demote a captured payment back to pending.
    /// </summary>
    public static bool SupersedesOrEquals(this PaymentStatus incoming, PaymentStatus current) =>
        incoming.Rank() >= current.Rank();
}
