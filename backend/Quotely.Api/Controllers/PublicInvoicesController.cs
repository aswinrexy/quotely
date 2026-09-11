using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Quotely.Api.DTOs;
using Quotely.Api.Services;

namespace Quotely.Api.Controllers;

/// <summary>
/// Customer-facing invoice endpoints. Anonymous by design: the payment token in the route is the
/// authorization, and it grants access to exactly one invoice. No route here accepts an internal
/// identifier, and there is no listing endpoint.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/public/invoices")]
public class PublicInvoicesController : ControllerBase
{
    private readonly IPublicInvoiceService _public;
    private readonly IPdfService _pdf;

    public PublicInvoicesController(IPublicInvoiceService publicInvoices, IPdfService pdf)
    {
        _public = publicInvoices;
        _pdf = pdf;
    }

    [HttpGet("{token}")]
    [ProducesResponseType(typeof(PublicInvoiceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PublicInvoiceDto>> Get(string token, CancellationToken ct)
        => Ok(await _public.GetAsync(token, ct));

    /// <summary>
    /// Registers a payment order for whatever is currently outstanding. The request body is
    /// empty on purpose: the amount is the server's to decide, never the browser's.
    /// </summary>
    [HttpPost("{token}/create-payment-order")]
    [ProducesResponseType(typeof(PaymentOrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PaymentOrderDto>> CreatePaymentOrder(string token, CancellationToken ct)
        => Ok(await _public.CreatePaymentOrderAsync(token, ct));

    /// <summary>Confirms a checkout result server-side and returns the authoritative balance.</summary>
    [HttpPost("{token}/verify-payment")]
    [ProducesResponseType(typeof(VerifyPaymentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<VerifyPaymentResponse>> VerifyPayment(
        string token, VerifyPaymentRequest request, CancellationToken ct)
        => Ok(await _public.VerifyPaymentAsync(token, request, ct));

    /// <summary>The same PDF the owner downloads, reached through the payment token.</summary>
    [HttpGet("{token}/pdf")]
    public async Task<IActionResult> Pdf(string token, CancellationToken ct)
    {
        var (invoice, business) = await _public.GetForPdfAsync(token, ct);
        var pdf = _pdf.GenerateInvoice(invoice, business);

        Response.Headers.Append("Access-Control-Expose-Headers", "Content-Disposition");
        return File(pdf.Content, "application/pdf", pdf.FileName);
    }
}
