using System.ComponentModel.DataAnnotations;
using Quotely.Api.Models;

namespace Quotely.Api.DTOs;

/// <summary>
/// A business's subscription to Quotely, as its owner may see it.
///
/// Nothing about the provider leaks through: no Razorpay subscription id, no plan id, no key.
/// The owner is told what they pay, what state they are in and when money next moves — which is
/// everything a person actually needs, and nothing that describes our integration.
/// </summary>
public class SubscriptionDto
{
    public string PlanCode { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string? PlanDescription { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = "INR";
    public string Interval { get; set; } = "monthly";

    public SubscriptionStatus Status { get; set; }
    public string? StatusMessage { get; set; }

    public DateTime? TrialStart { get; set; }
    public DateTime? TrialEnd { get; set; }
    public bool InTrial { get; set; }

    public DateTime? CurrentPeriodStart { get; set; }
    public DateTime? CurrentPeriodEnd { get; set; }

    public DateTime? CancelRequestedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public DateTime? LastPaymentAt { get; set; }

    /// <summary>Whether a payment mandate has been set up. Not whether it has charged yet.</summary>
    public bool HasActiveMandate { get; set; }

    public bool HasAccess { get; set; }
    public DateTime? AccessEndsAt { get; set; }
    public DateTime? NextPaymentAt { get; set; }

    /// <summary>False when this deployment does not charge for anything.</summary>
    public bool BillingEnabled { get; set; }

    public string? CouponCode { get; set; }
    public DateTime? CouponRedeemedAt { get; set; }
    public int? CouponFreeMonths { get; set; }
}

/// <summary>What the browser needs to open Razorpay's subscription checkout.</summary>
public class SubscriptionCheckoutDto
{
    /// <summary>Quotely's OWN publishable key. Never a merchant's.</summary>
    public string KeyId { get; set; } = string.Empty;
    public string SubscriptionId { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Currency { get; set; } = "INR";

    /// <summary>When billing actually begins. Null means immediately.</summary>
    public DateTime? FirstChargeAt { get; set; }

    /// <summary>Razorpay's hosted authorisation page, when it offers one.</summary>
    public string? ShortUrl { get; set; }
}

/// <summary>A coupon code, as typed. Validated entirely on the server.</summary>
public class RedeemCouponRequest
{
    [Required(ErrorMessage = "Enter a coupon code.")]
    [StringLength(40, MinimumLength = 3)]
    public string Code { get; set; } = string.Empty;
}

/// <summary>What Razorpay's subscription checkout hands back. Every field is untrusted.</summary>
public class ConfirmSubscriptionRequest
{
    [Required]
    [StringLength(80)]
    public string RazorpaySubscriptionId { get; set; } = string.Empty;

    [Required]
    [StringLength(80)]
    public string RazorpayPaymentId { get; set; } = string.Empty;

    [Required]
    [StringLength(256)]
    public string RazorpaySignature { get; set; } = string.Empty;
}
