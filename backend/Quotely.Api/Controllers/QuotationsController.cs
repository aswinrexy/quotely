using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Quotely.Api.Data;
using Quotely.Api.DTOs;
using Quotely.Api.Billing;
using Quotely.Api.Services;

namespace Quotely.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/quotations")]
public class QuotationsController : ControllerBase
{
    private readonly IQuotationService _quotations;
    private readonly IPublicQuotationService _publicQuotations;
    private readonly IInvoiceService _invoices;
    private readonly IPdfService _pdf;
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public QuotationsController(
        IQuotationService quotations,
        IPublicQuotationService publicQuotations,
        IInvoiceService invoices,
        IPdfService pdf,
        AppDbContext db,
        ICurrentUser currentUser)
    {
        _quotations = quotations;
        _publicQuotations = publicQuotations;
        _invoices = invoices;
        _pdf = pdf;
        _db = db;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<QuotationListItemDto>>> List(
        [FromQuery] string? search, [FromQuery] string? status,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] Guid? customerId = null, CancellationToken ct = default)
        => Ok(await _quotations.ListAsync(_currentUser.Id, search, status, page, pageSize, customerId, ct));

    [HttpGet("stats")]
    public async Task<ActionResult<DashboardStatsDto>> Stats(CancellationToken ct)
        => Ok(await _quotations.GetDashboardAsync(_currentUser.Id, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<QuotationDto>> Get(Guid id, CancellationToken ct)
        => Ok(await _quotations.GetAsync(_currentUser.Id, id, ct));

    /// <summary>
    /// Gated on the subscription. This is one of exactly three places in the application where
    /// that happens — creating a quotation, creating an invoice, and raising an invoice from a
    /// quotation — and the rule itself lives in ISubscriptionEntitlementService, not here.
    /// </summary>
    [RequiresEntitlement(Entitlement.CreateQuotation)]
    [HttpPost]
    public async Task<ActionResult<QuotationDto>> Create(SaveQuotationRequest request, CancellationToken ct)
    {
        var created = await _quotations.CreateAsync(_currentUser.Id, request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<QuotationDto>> Update(Guid id, SaveQuotationRequest request, CancellationToken ct)
        => Ok(await _quotations.UpdateAsync(_currentUser.Id, id, request, ct));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _quotations.DeleteAsync(_currentUser.Id, id, ct);
        return NoContent();
    }

    /// <summary>
    /// Creates the customer-facing share link for one of the caller's quotations and returns the
    /// full URL. The raw token is only ever present in this response — the database keeps a hash —
    /// so calling this again issues a fresh link and retires the previous one.
    /// </summary>
    [HttpPost("{id:guid}/public-link")]
    [ProducesResponseType(typeof(PublicQuotationLinkDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PublicQuotationLinkDto>> CreatePublicLink(Guid id, CancellationToken ct)
        => Ok(await _publicQuotations.CreateLinkAsync(_currentUser.Id, id, ct));

    /// <summary>
    /// Raises the invoice for an accepted quotation. Only Accepted quotations convert, and only
    /// once: a second call reports the existing invoice as a conflict rather than duplicating it.
    /// </summary>
    /// <summary>Gated on the subscription; see the note on QuotationsController.Create.</summary>
    [RequiresEntitlement(Entitlement.CreateInvoice)]
    [HttpPost("{id:guid}/convert-to-invoice")]
    [ProducesResponseType(typeof(InvoiceDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<InvoiceDto>> ConvertToInvoice(Guid id, CancellationToken ct)
    {
        var invoice = await _invoices.ConvertFromQuotationAsync(_currentUser.Id, id, ct);
        return Created($"/api/invoices/{invoice.Id}", invoice);
    }

    /// <summary>Generates the PDF on the fly. Nothing is stored server-side.</summary>
    [HttpPost("{id:guid}/pdf")]
    [HttpGet("{id:guid}/pdf")]
    public async Task<IActionResult> GeneratePdf(Guid id, CancellationToken ct)
    {
        var userId = _currentUser.Id;
        var quotation = await _quotations.GetEntityForPdfAsync(userId, id, ct);
        var business = await _db.BusinessProfiles.AsNoTracking().FirstOrDefaultAsync(b => b.UserId == userId, ct);

        var pdf = _pdf.Generate(quotation, business);

        Response.Headers.Append("Access-Control-Expose-Headers", "Content-Disposition");
        return File(pdf.Content, "application/pdf", pdf.FileName);
    }
}
