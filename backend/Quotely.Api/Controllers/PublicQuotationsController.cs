using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Quotely.Api.DTOs;
using Quotely.Api.Models;
using Quotely.Api.Services;

namespace Quotely.Api.Controllers;

/// <summary>
/// Customer-facing endpoints. These are intentionally anonymous: the share token in the route
/// is the authorization. It grants access to exactly one quotation and nothing else — there is
/// no listing endpoint here, and no route accepts an internal identifier.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/public/quotations")]
public class PublicQuotationsController : ControllerBase
{
    private readonly IPublicQuotationService _public;
    private readonly IPdfService _pdf;

    public PublicQuotationsController(IPublicQuotationService publicQuotations, IPdfService pdf)
    {
        _public = publicQuotations;
        _pdf = pdf;
    }

    [HttpGet("{token}")]
    [ProducesResponseType(typeof(PublicQuotationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PublicQuotationDto>> Get(string token, CancellationToken ct)
        => Ok(await _public.GetAsync(token, ct));

    [HttpPost("{token}/accept")]
    [ProducesResponseType(typeof(PublicQuotationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PublicQuotationDto>> Accept(string token, PublicResponseRequest request, CancellationToken ct)
        => Ok(await _public.RespondAsync(token, QuotationStatus.Accepted, request, ct));

    [HttpPost("{token}/reject")]
    [ProducesResponseType(typeof(PublicQuotationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PublicQuotationDto>> Reject(string token, PublicResponseRequest request, CancellationToken ct)
        => Ok(await _public.RespondAsync(token, QuotationStatus.Rejected, request, ct));

    /// <summary>Same PDF the owner downloads, reached through the share token instead of a JWT.</summary>
    [HttpGet("{token}/pdf")]
    public async Task<IActionResult> Pdf(string token, CancellationToken ct)
    {
        var (quotation, business) = await _public.GetForPdfAsync(token, ct);
        var pdf = _pdf.Generate(quotation, business);

        Response.Headers.Append("Access-Control-Expose-Headers", "Content-Disposition");
        return File(pdf.Content, "application/pdf", pdf.FileName);
    }
}
