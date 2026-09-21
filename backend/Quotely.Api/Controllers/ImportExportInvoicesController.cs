using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Quotely.Api.Billing;
using Quotely.Api.Data;
using Quotely.Api.DTOs;
using Quotely.Api.Services;

namespace Quotely.Api.Controllers;

/// <summary>
/// Import/export invoices and proforma invoices.
///
/// A separate route from <c>/api/invoices</c> because the document shape is genuinely different,
/// but NOT a separate subsystem: these are Invoice rows, so the public link, payment history,
/// manual payments and receivables endpoints on InvoicesController work against them unchanged.
/// Nothing here duplicates any of that.
///
/// Every action resolves the tenant from the validated JWT via <see cref="ICurrentUser"/>. Nothing
/// is ever taken from the request body, and a document belonging to another business — or a plain
/// domestic invoice reached through these routes — is a 404.
/// </summary>
[ApiController]
[Authorize]
[Route("api/import-export/invoices")]
public class ImportExportInvoicesController : ControllerBase
{
    private readonly ITradeInvoiceService _invoices;
    private readonly IPublicInvoiceService _publicInvoices;
    private readonly IPdfService _pdf;
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public ImportExportInvoicesController(
        ITradeInvoiceService invoices,
        IPublicInvoiceService publicInvoices,
        IPdfService pdf,
        AppDbContext db,
        ICurrentUser currentUser)
    {
        _invoices = invoices;
        _publicInvoices = publicInvoices;
        _pdf = pdf;
        _db = db;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<TradeInvoiceListItemDto>>> List(
        [FromQuery] string? search,
        [FromQuery] string? tradeType,
        [FromQuery] string? documentType,
        [FromQuery] string? status,
        [FromQuery] Guid? customerId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
        => Ok(await _invoices.ListAsync(
            _currentUser.Id, search, tradeType, documentType, status, customerId, page, pageSize, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TradeInvoiceDto>> Get(Guid id, CancellationToken ct)
        => Ok(await _invoices.GetAsync(_currentUser.Id, id, ct));

    /// <summary>
    /// Gated on the subscription by the same entitlement a domestic invoice uses. A trade document
    /// is an invoice and counts as one — giving it its own allowance would be a way around the
    /// invoice limit, not a separate product.
    /// </summary>
    [RequiresEntitlement(Entitlement.CreateInvoice)]
    [HttpPost]
    [ProducesResponseType(typeof(TradeInvoiceDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TradeInvoiceDto>> Create(
        SaveTradeInvoiceRequest request, CancellationToken ct)
    {
        var created = await _invoices.CreateAsync(_currentUser.Id, request, ct);
        return Created($"/api/import-export/invoices/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(TradeInvoiceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TradeInvoiceDto>> Update(
        Guid id, SaveTradeInvoiceRequest request, CancellationToken ct)
        => Ok(await _invoices.UpdateAsync(_currentUser.Id, id, request, ct));

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _invoices.DeleteAsync(_currentUser.Id, id, ct);
        return NoContent();
    }

    /// <summary>
    /// The customer-facing link, minted by the very same service the domestic invoice uses — one
    /// token scheme, one hashing rule, one public page. A proforma shared this way is readable and
    /// downloadable but shows no Pay button, because <c>Invoice.AcceptsPayments</c> refuses it.
    /// </summary>
    [HttpPost("{id:guid}/public-link")]
    [ProducesResponseType(typeof(PublicInvoiceLinkDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PublicInvoiceLinkDto>> CreatePublicLink(Guid id, CancellationToken ct)
    {
        // Resolve through the trade service first, so a domestic invoice cannot be linked from
        // this route and a document belonging to someone else is a 404 before anything is minted.
        await _invoices.GetAsync(_currentUser.Id, id, ct);
        return Ok(await _publicInvoices.CreateLinkAsync(_currentUser.Id, id, ct));
    }

    /// <summary>Renders the stored snapshot through the import/export layout.</summary>
    [HttpPost("{id:guid}/pdf")]
    [HttpGet("{id:guid}/pdf")]
    public async Task<IActionResult> GeneratePdf(Guid id, CancellationToken ct)
    {
        var userId = _currentUser.Id;
        var invoice = await _invoices.GetEntityForPdfAsync(userId, id, ct);
        var business = await _db.BusinessProfiles.AsNoTracking()
            .FirstOrDefaultAsync(b => b.UserId == userId, ct);

        var pdf = _pdf.GenerateTradeInvoice(invoice, business);
        return File(pdf.Content, "application/pdf", pdf.FileName);
    }
}

/// <summary>
/// The business's import/export settings: the identifiers and defaults that would otherwise be
/// retyped on every document.
/// </summary>
[ApiController]
[Authorize]
[Route("api/import-export/profile")]
public class ImportExportProfileController : ControllerBase
{
    private readonly ITradeProfileService _profiles;
    private readonly ICurrentUser _currentUser;

    public ImportExportProfileController(ITradeProfileService profiles, ICurrentUser currentUser)
    {
        _profiles = profiles;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<ActionResult<TradeProfileDto>> Get(CancellationToken ct)
        => Ok(await _profiles.GetAsync(_currentUser.Id, ct));

    [HttpPut]
    public async Task<ActionResult<TradeProfileDto>> Save(
        SaveTradeProfileRequest request, CancellationToken ct)
        => Ok(await _profiles.SaveAsync(_currentUser.Id, request, ct));

    /// <summary>
    /// What a blank document should already contain, resolved through trade profile then business
    /// profile. Read-only: this creates nothing and is safe to call on every form load.
    /// </summary>
    [HttpGet("defaults")]
    public async Task<ActionResult<TradeInvoiceDefaultsDto>> Defaults(CancellationToken ct)
        => Ok(await _profiles.GetDefaultsAsync(_currentUser.Id, ct));
}
