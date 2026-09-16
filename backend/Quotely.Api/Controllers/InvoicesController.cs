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

    /// <summary>
    /// Raises an invoice directly, without a quotation (V2.4). The result is an ordinary invoice:
    /// the same numbering sequence, lifecycle, PDF, payment link and payment flow as one converted
    /// from a quotation. The customer must belong to the caller.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(InvoiceDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<InvoiceDto>> Create(CreateInvoiceRequest request, CancellationToken ct)
    {
        var created = await _invoices.CreateAsync(_currentUser.Id, request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

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

    /// <summary>
    /// What the business is owed across every issued invoice, plus the few worth chasing first.
    /// Aggregated by the database — the dashboard does not add invoices up in the browser.
    /// </summary>
    [HttpGet("stats")]
    [ProducesResponseType(typeof(ReceivablesDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ReceivablesDto>> Stats(
        [FromQuery] int needsAttention = 5, CancellationToken ct = default)
        => Ok(await _invoices.GetReceivablesAsync(_currentUser.Id, needsAttention, ct));

    /// <summary>Payment history and the derived financial summary for one of the caller's invoices.</summary>
    [HttpGet("{id:guid}/payments")]
    [ProducesResponseType(typeof(InvoicePaymentsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<InvoicePaymentsDto>> Payments(Guid id, CancellationToken ct)
        => Ok(await _payments.GetInvoicePaymentsAsync(_currentUser.Id, id, ct));

    /// <summary>
    /// Records money received outside the gateway — cash, a bank transfer, a UPI transfer, a
    /// cheque (V2.5). It joins the same ledger a Razorpay capture does; the amount is validated
    /// against an outstanding balance the server computes, never one the browser supplies.
    /// </summary>
    [HttpPost("{id:guid}/payments")]
    [ProducesResponseType(typeof(PaymentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PaymentDto>> RecordPayment(
        Guid id, RecordManualPaymentRequest request, CancellationToken ct)
    {
        var payment = await _payments.RecordManualPaymentAsync(_currentUser.Id, id, request, ct);
        return Created($"/api/invoices/{id}/payments", payment);
    }

    /// <summary>
    /// Stops a manual payment counting toward the balance while keeping it on the record. Only
    /// manual payments can be voided: gateway money is the provider's record, and correcting it
    /// means a refund, which this version does not perform.
    /// </summary>
    [HttpPost("{id:guid}/payments/{paymentId:guid}/void")]
    [ProducesResponseType(typeof(PaymentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PaymentDto>> VoidPayment(Guid id, Guid paymentId, CancellationToken ct)
        => Ok(await _payments.VoidPaymentAsync(_currentUser.Id, id, paymentId, ct));

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
