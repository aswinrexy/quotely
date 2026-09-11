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

    [HttpPost("razorpay")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Razorpay(CancellationToken ct)
    {
        // The body is read as a string and passed through untouched. Razorpay signs the exact
        // bytes it sent, so deserialising and re-serialising first — which reorders keys and
        // changes whitespace — would break every signature check.
        using var reader = new StreamReader(Request.Body);
        var rawBody = await reader.ReadToEndAsync(ct);

        var accepted = await _webhooks.HandleRazorpayAsync(
            rawBody,
            Request.Headers["X-Razorpay-Signature"].FirstOrDefault(),
            Request.Headers["X-Razorpay-Event-Id"].FirstOrDefault(),
            ct);

        // Deliberately terse: a webhook response is not a place to describe internal state.
        return accepted ? Ok(new { status = "ok" }) : BadRequest(new { status = "rejected" });
    }
}
