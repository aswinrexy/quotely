using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Quotely.Api.Billing;
using Quotely.Api.DTOs;
using Quotely.Api.Services;

namespace Quotely.Api.Controllers;

/// <summary>
/// Settings → Billing. What this business pays QUOTELY.
///
/// Not to be confused with anything under /api/settings/payments, which is about what this
/// business collects from ITS OWN customers. Different money, different account, different
/// controller — see docs/architecture.md, "Three identities".
///
/// Every action works on the tenant from the validated JWT. Nothing here is gated on the
/// subscription itself: an account whose subscription lapsed must always be able to reach the
/// page that would fix that.
/// </summary>
[ApiController]
[Authorize]
[Route("api/billing")]
public class BillingController : ControllerBase
{
    private readonly ISubscriptionService _subscriptions;
    private readonly ISubscriptionEntitlementService _entitlements;
    private readonly ICurrentUser _currentUser;

    public BillingController(
        ISubscriptionService subscriptions,
        ISubscriptionEntitlementService entitlements,
        ICurrentUser currentUser)
    {
        _subscriptions = subscriptions;
        _entitlements = entitlements;
        _currentUser = currentUser;
    }

    /// <summary>The current plan, status and dates.</summary>
    [HttpGet("subscription")]
    public async Task<ActionResult<SubscriptionDto>> Get(CancellationToken ct) =>
        Ok(await _subscriptions.GetStatusAsync(_currentUser.Id, ct));

    /// <summary>
    /// What this business may currently do, and how much of each allowance is left.
    ///
    /// The interface reads this to decide what to show as locked. It is advisory: every gate is
    /// enforced again on the endpoint that does the work, because a browser can be told anything.
    /// </summary>
    [HttpGet("entitlements")]
    public async Task<ActionResult<EntitlementSummary>> Entitlements(CancellationToken ct) =>
        Ok(await _entitlements.DescribeAsync(_currentUser.Id, ct));

    /// <summary>
    /// Redeems a coupon. The server decides what the code is worth; the browser only carries the
    /// characters that were typed.
    /// </summary>
    [HttpPost("coupon")]
    public async Task<ActionResult<SubscriptionDto>> Redeem(RedeemCouponRequest request, CancellationToken ct) =>
        Ok(await _subscriptions.RedeemCouponAsync(_currentUser.Id, request.Code, ct));

    /// <summary>Sets up the subscription at Razorpay and returns what checkout needs.</summary>
    [HttpPost("subscription")]
    public async Task<ActionResult<SubscriptionCheckoutDto>> Start(CancellationToken ct) =>
        Ok(await _subscriptions.StartAsync(_currentUser.Id, ct));

    /// <summary>Confirms the mandate the business authorised.</summary>
    [HttpPost("subscription/confirm")]
    public async Task<ActionResult<SubscriptionDto>> Confirm(
        ConfirmSubscriptionRequest request, CancellationToken ct) =>
        Ok(await _subscriptions.ConfirmAsync(_currentUser.Id, request, ct));

    /// <summary>Cancels at the end of the period already paid for.</summary>
    [HttpDelete("subscription")]
    public async Task<ActionResult<SubscriptionDto>> Cancel(CancellationToken ct) =>
        Ok(await _subscriptions.CancelAsync(_currentUser.Id, ct));
}
