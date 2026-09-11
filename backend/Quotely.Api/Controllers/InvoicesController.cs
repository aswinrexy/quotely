using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Quotely.Api.Data;
using Quotely.Api.DTOs;
using Quotely.Api.Services;

namespace Quotely.Api.Controllers;

/// <summary>
/// Invoices are private to the business that raised them. Every action resolves the tenant from
/// the validated JWT via <see cref="ICurrentUser"/>; nothing is taken from the request body.
/// </summary>
[ApiController]
[Authorize]
[Route("api/invoices")]
public class InvoicesController : ControllerBase
{
    private readonly IInvoiceService _invoices;
    private readonly IPublicInvoiceService _publicInvoices;
    private readonly IPaymentService _payments;
    private readonly IPdfService _pdf;
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public InvoicesController(
        IInvoiceService invoices,
        IPublicInvoiceService publicInvoices,
        IPaymentService payments,
        IPdfService pdf,
        AppDbContext db,
        ICurrentUser currentUser)
    {
        _invoices = invoices;
        _publicInvoices = publicInvoices;
        _payments = payments;
        _pdf = pdf;
        _db = db;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<InvoiceListItemDto>>> List(
        [FromQuery] string? search, [FromQuery] string? status,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => Ok(await _invoices.ListAsync(_currentUser.Id, search, status, page, pageSize, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<InvoiceDto>> Get(Guid id, CancellationToken ct)
        => Ok(await _invoices.GetAsync(_currentUser.Id, id, ct));

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<InvoiceDto>> Update(Guid id, SaveInvoiceRequest request, CancellationToken ct)
        => Ok(await _invoices.UpdateAsync(_currentUser.Id, id, request, ct));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _invoices.DeleteAsync(_currentUser.Id, id, ct);
        return NoContent();
    }

    /// <summary>
    /// Creates the customer-facing payment link for one of the caller's own invoices and returns
    /// the full URL. Only a hash is stored, so this response is the one and only time the URL
    /// exists — calling again issues a fresh link and retires the previous one.
    /// </summary>
    [HttpPost("{id:guid}/public-link")]
    [ProducesResponseType(typeof(PublicInvoiceLinkDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PublicInvoiceLinkDto>> CreatePublicLink(Guid id, CancellationToken ct)
        => Ok(await _publicInvoices.CreateLinkAsync(_currentUser.Id, id, ct));

    /// <summary>Payment history and the derived financial summary for one of the caller's invoices.</summary>
    [HttpGet("{id:guid}/payments")]
    [ProducesResponseType(typeof(InvoicePaymentsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<InvoicePaymentsDto>> Payments(Guid id, CancellationToken ct)
        => Ok(await _payments.GetInvoicePaymentsAsync(_currentUser.Id, id, ct));

    /// <summary>Renders the stored snapshot. Nothing is read from the live catalogue.</summary>
    [HttpPost("{id:guid}/pdf")]
    [HttpGet("{id:guid}/pdf")]
    public async Task<IActionResult> GeneratePdf(Guid id, CancellationToken ct)
    {
        var userId = _currentUser.Id;
        var invoice = await _invoices.GetEntityForPdfAsync(userId, id, ct);
        var business = await _db.BusinessProfiles.AsNoTracking().FirstOrDefaultAsync(b => b.UserId == userId, ct);

        var pdf = _pdf.GenerateInvoice(invoice, business);

        Response.Headers.Append("Access-Control-Expose-Headers", "Content-Disposition");
        return File(pdf.Content, "application/pdf", pdf.FileName);
    }
}
