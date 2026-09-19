using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Quotely.Api.Billing;
using Quotely.Api.Services;

namespace Quotely.Api.Controllers;

/// <summary>
/// Provider webhooks. Anonymous to our JWT scheme — these requests come from the payment
/// provider, not from a signed-in user — and authenticated instead by a signature computed over
/// the raw request body.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/webhooks")]
public class WebhooksController : ControllerBase
{
    private readonly IWebhookService _webhooks;
    private readonly ISubscriptionWebhookService _billing;

    public WebhooksController(IWebhookService webhooks, ISubscriptionWebhookService billing)
    {
        _webhooks = webhooks;
        _billing = billing;
    }

    /// <summary>Reads the body as the provider sent it. Both handlers need the exact bytes.</summary>
    private async Task<string> RawBodyAsync(CancellationToken ct)
    {
        // Razorpay signs the exact bytes it sent, so deserialising and re-serialising first —
        // which reorders keys and changes whitespace — would break every signature check.
        using var reader = new StreamReader(Request.Body);
        return await reader.ReadToEndAsync(ct);
    }

    /// <summary>
    /// A merchant's payment webhook. The route token in the URL is what says which business the
    /// delivery is for — it is unguessable, specific to one connection, and reissued whenever a
    /// business disconnects.
    ///
    /// Putting it in the path rather than reading the account out of the body is deliberate: the
    /// body is not authenticated until its signature has been checked, and the signature cannot
    /// be checked until a secret has been chosen. Choosing that secret from the unverified body
    /// would be letting the attacker pick the key their forgery is verified against.
    /// </summary>
    [HttpPost("razorpay/m/{routeToken}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RazorpayMerchant(string routeToken, CancellationToken ct)
    {
        var rawBody = await RawBodyAsync(ct);

        var accepted = await _webhooks.HandleMerchantAsync(
            routeToken,
            rawBody,
            Request.Headers["X-Razorpay-Signature"].FirstOrDefault(),
            Request.Headers["X-Razorpay-Event-Id"].FirstOrDefault(),
            ct);

        // Deliberately terse: a webhook response is not a place to describe internal state.
        return accepted ? Ok(new { status = "ok" }) : BadRequest(new { status = "rejected" });
    }

    /// <summary>
    /// QUOTELY'S OWN subscription webhooks — a business paying us, not a customer paying them.
    ///
    /// A separate path with a separate handler and a separate secret. The merchant endpoint above
    /// cannot accept one of these and this cannot accept one of those, which is the point: one
    /// bug in either must not be able to move the other kind of money.
    /// </summary>
    [HttpPost("razorpay/billing")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RazorpayBilling(CancellationToken ct)
    {
        var rawBody = await RawBodyAsync(ct);

        var accepted = await _billing.HandleAsync(
            rawBody,
            Request.Headers["X-Razorpay-Signature"].FirstOrDefault(),
            Request.Headers["X-Razorpay-Event-Id"].FirstOrDefault(),
            ct);

        return accepted ? Ok(new { status = "ok" }) : BadRequest(new { status = "rejected" });
    }
}
