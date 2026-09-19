using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

    public WebhooksController(IWebhookService webhooks) => _webhooks = webhooks;

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
        // The body is read as a string and passed through untouched. Razorpay signs the exact
        // bytes it sent, so deserialising and re-serialising first — which reorders keys and
        // changes whitespace — would break every signature check.
        using var reader = new StreamReader(Request.Body);
        var rawBody = await reader.ReadToEndAsync(ct);

        var accepted = await _webhooks.HandleMerchantAsync(
            routeToken,
            rawBody,
            Request.Headers["X-Razorpay-Signature"].FirstOrDefault(),
            Request.Headers["X-Razorpay-Event-Id"].FirstOrDefault(),
            ct);

        // Deliberately terse: a webhook response is not a place to describe internal state.
        return accepted ? Ok(new { status = "ok" }) : BadRequest(new { status = "rejected" });
    }
}
