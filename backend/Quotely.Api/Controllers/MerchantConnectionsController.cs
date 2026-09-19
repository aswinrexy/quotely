using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Quotely.Api.Billing;
using Quotely.Api.DTOs;
using Quotely.Api.Services;

namespace Quotely.Api.Controllers;

/// <summary>
/// Settings → Payments. Where a business attaches its OWN Razorpay account so that its customers'
/// invoice payments land in its own bank account rather than anywhere else.
///
/// Every action operates on <c>ICurrentUser.Id</c> — the tenant from the validated JWT. No route
/// or body on this controller names a connection, so there is no request shape that could ask to
/// act on somebody else's payment account.
/// </summary>
[ApiController]
[Authorize]
[Route("api/settings/payments")]
public class MerchantConnectionsController : ControllerBase
{
    private readonly IMerchantConnectionService _connections;
    private readonly ICurrentUser _currentUser;

    public MerchantConnectionsController(IMerchantConnectionService connections, ICurrentUser currentUser)
    {
        _connections = connections;
        _currentUser = currentUser;
    }

    /// <summary>The current state of this business's payment connection. Never includes a secret.</summary>
    [HttpGet("connection")]
    public async Task<ActionResult<MerchantConnectionDto>> Get(CancellationToken ct) =>
        Ok(await _connections.GetStatusAsync(_currentUser.Id, ct));

    /// <summary>
    /// Attaches a Razorpay account from the merchant's own API keys. The response carries the
    /// webhook secret once and only once.
    /// </summary>
    /// <summary>Gated on the subscription; the rule lives in ISubscriptionEntitlementService.</summary>
    [RequiresEntitlement(Entitlement.ConnectPayments)]
    [HttpPost("razorpay/keys")]
    public async Task<ActionResult<MerchantConnectionDto>> ConnectKeys(
        ConnectRazorpayKeysRequest request, CancellationToken ct) =>
        Ok(await _connections.ConnectKeyPairAsync(_currentUser.Id, request, ct));

    /// <summary>
    /// Begins "Connect with Razorpay". Returns the URL to send the merchant to; the browser
    /// navigates there itself rather than being redirected by us, so a failure is a visible error
    /// on the settings page instead of an opaque 302.
    /// </summary>
    /// <summary>Gated on the subscription; the rule lives in ISubscriptionEntitlementService.</summary>
    [RequiresEntitlement(Entitlement.ConnectPayments)]
    [HttpPost("razorpay/oauth/start")]
    public async Task<ActionResult<MerchantConnectionStartDto>> StartOauth(CancellationToken ct) =>
        Ok(await _connections.StartOauthAsync(_currentUser.Id, ct));

    /// <summary>
    /// Completes the authorisation. Called by the settings page with the code and state Razorpay
    /// put on the callback URL — authenticated as the signed-in owner, so a code cannot be
    /// redeemed by whoever happens to find it.
    /// </summary>
    [HttpPost("razorpay/oauth/complete")]
    public async Task<ActionResult<MerchantConnectionDto>> CompleteOauth(
        CompleteRazorpayOauthRequest request, CancellationToken ct) =>
        Ok(await _connections.CompleteOauthAsync(_currentUser.Id, request.Code, request.State, ct));

    /// <summary>
    /// Detaches the account and destroys every credential we hold for it. Invoices stop being
    /// payable online immediately; nothing already paid is affected.
    /// </summary>
    [HttpDelete("razorpay")]
    public async Task<ActionResult<MerchantConnectionDto>> Disconnect(CancellationToken ct) =>
        Ok(await _connections.DisconnectAsync(_currentUser.Id, ct));
}
