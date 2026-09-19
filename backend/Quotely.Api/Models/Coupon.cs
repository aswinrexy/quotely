namespace Quotely.Api.Models;

/// <summary>
/// A code that grants free months of Quotely.
///
/// The benefit is enforced here and in <see cref="CouponRedemption"/>, not in the price shown on
/// a page. A coupon that only changed a displayed figure would be a coupon anyone could apply by
/// editing their own browser — the server decides what a code is worth, every time.
/// </summary>
public class Coupon
{
    public Guid Id { get; set; }

    /// <summary>
    /// The code as typed. Stored upper-cased and compared that way, so QUOTELY6 and quotely6 are
    /// the same coupon rather than one working and one mystifying whoever typed it.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>What the owner is told they are getting.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>How many months of free access this grants.</summary>
    public int FreeMonths { get; set; }

    /// <summary>
    /// How many accounts may redeem it in total, or null for no limit. Checked inside the same
    /// transaction as the redemption, so a limited coupon cannot be over-redeemed by two requests
    /// arriving at once.
    /// </summary>
    public int? MaxRedemptions { get; set; }

    /// <summary>
    /// How many times it has been redeemed. Maintained alongside the redemption rows rather than
    /// counted from them on demand: the count is what the limit is checked against, and checking
    /// it needs to be one cheap indexed read inside a transaction.
    /// </summary>
    public int RedemptionCount { get; set; }

    /// <summary>After this, the code stops working. Null means it does not expire.</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>Lets a code be withdrawn without deleting the record of who used it.</summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public ICollection<CouponRedemption> Redemptions { get; set; } = new List<CouponRedemption>();

    /// <summary>
    /// Whether the code could be redeemed at all right now, ignoring who is asking. The
    /// per-account rule — one redemption each — is a uniqueness constraint rather than a check,
    /// because a check can be passed by two requests at the same moment.
    /// </summary>
    public bool IsRedeemable(DateTime now) =>
        IsActive &&
        (ExpiresAt is null || ExpiresAt > now) &&
        (MaxRedemptions is null || RedemptionCount < MaxRedemptions);
}

/// <summary>
/// One account's use of one coupon. The audit trail, and — through a unique index on
/// (CouponId, UserId) — the enforcement of "once per account".
///
/// That constraint is deliberately in the database rather than in a service method. Two
/// simultaneous redemption requests can both pass an "already redeemed?" check; only one of them
/// can win an insert.
/// </summary>
public class CouponRedemption
{
    public Guid Id { get; set; }

    public Guid CouponId { get; set; }
    public Coupon? Coupon { get; set; }

    public Guid UserId { get; set; }
    public AppUser? User { get; set; }

    public Guid SubscriptionId { get; set; }
    public Subscription? Subscription { get; set; }

    /// <summary>
    /// What this redemption actually granted, recorded rather than recomputed. If the coupon is
    /// later edited, the record of what somebody was given must not change with it.
    /// </summary>
    public int FreeMonthsGranted { get; set; }

    /// <summary>The trial end date before and after, so the effect is reconstructable.</summary>
    public DateTime? TrialEndBefore { get; set; }
    public DateTime? TrialEndAfter { get; set; }

    public DateTime RedeemedAt { get; set; } = DateTime.UtcNow;

    /// <summary>The code as typed, kept for the audit trail even if the coupon is renamed.</summary>
    public string CodeUsed { get; set; } = string.Empty;
}

/// <summary>The codes Quotely ships with. Seeded once; never re-created if already present.</summary>
public static class WellKnownCoupons
{
    /// <summary>Six months free, one per account. The launch offer.</summary>
    public const string SixMonthsFree = "QUOTELY6";
}
