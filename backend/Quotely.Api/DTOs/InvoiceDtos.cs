using System.ComponentModel.DataAnnotations;
using Quotely.Api.Models;

namespace Quotely.Api.DTOs;

public record InvoiceItemDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Unit { get; init; } = "Service";
    public decimal Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal Discount { get; init; }
    public decimal TaxRate { get; init; }
    public decimal LineSubtotal { get; init; }
    public decimal LineTax { get; init; }
    public decimal LineTotal { get; init; }
}

public record InvoiceListItemDto
{
    public Guid Id { get; init; }
    public string InvoiceNumber { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
    public DateOnly InvoiceDate { get; init; }
    public DateOnly DueDate { get; init; }
    public string Status { get; init; } = nameof(InvoiceStatus.Draft);
    public decimal GrandTotal { get; init; }
    public string Currency { get; init; } = "INR";
    /// <summary>Summed from payments that count; never a stored column.</summary>
    public decimal Paid { get; init; }
    public decimal Outstanding { get; init; }
    /// <summary>
    /// Issued, past its due date and still owing something. Derived per request rather than
    /// stored, so it becomes true the day it should without anything having to run.
    /// </summary>
    public bool IsOverdue { get; init; }
    /// <summary>The source quotation's number, or empty when the invoice was raised directly.</summary>
    public string QuotationNumber { get; init; } = string.Empty;
}

public record InvoiceDto
{
    public Guid Id { get; init; }
    public string InvoiceNumber { get; init; } = string.Empty;

    /// <summary>
    /// The quotation this invoice came from, so the owner can navigate back. Null for an invoice
    /// raised directly — the only thing that distinguishes the two creation paths.
    /// </summary>
    public Guid? QuotationId { get; init; }
    public string QuotationNumber { get; init; } = string.Empty;

    /// <summary>Billing details as they stood when the invoice was raised.</summary>
    public InvoiceCustomerDto Customer { get; init; } = new();
    /// <summary>Live business profile — the issuer's own letterhead, not part of the snapshot.</summary>
    public BusinessProfileDto? Business { get; init; }

    public DateOnly InvoiceDate { get; init; }
    public DateOnly DueDate { get; init; }
    public string Status { get; init; } = nameof(InvoiceStatus.Draft);
    public bool IsOverdue { get; init; }
    /// <summary>False once the invoice is Paid or Cancelled.</summary>
    public bool CanEdit { get; init; }
    /// <summary>Line items and totals may only be rewritten while the invoice is a draft.</summary>
    public bool CanEditItems { get; init; }
    public bool CanDelete { get; init; }

    public string? Notes { get; init; }
    public string? Terms { get; init; }

    public decimal Subtotal { get; init; }
    public decimal DiscountTotal { get; init; }
    public decimal TaxTotal { get; init; }
    public decimal GrandTotal { get; init; }
    public string Currency { get; init; } = "INR";

    public IReadOnlyList<InvoiceItemDto> Items { get; init; } = Array.Empty<InvoiceItemDto>();

    // ---- payments (V2.3) ----
    /// <summary>Summed from captured payments; never a stored, editable figure.</summary>
    public decimal Paid { get; init; }
    public decimal Outstanding { get; init; }
    /// <summary>Whether a payment link is currently active. The URL itself cannot be shown again.</summary>
    public bool HasPublicLink { get; init; }
    public DateTime? PublicLinkCreatedAt { get; init; }

    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

/// <summary>The customer snapshot stored on the invoice. Not the live Customer record.</summary>
public record InvoiceCustomerDto
{
    public string Name { get; init; } = string.Empty;
    public string? CompanyName { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? AddressLine { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? PostalCode { get; init; }
    public string? Country { get; init; }
}

public record SaveInvoiceItemRequest
{
    [Required, MaxLength(200)]
    public string Name { get; init; } = string.Empty;

    [MaxLength(1000)] public string? Description { get; init; }

    [Required, MaxLength(50)]
    public string Unit { get; init; } = "Service";

    [Range(0.0001, 1_000_000)]
    public decimal Quantity { get; init; }

    [Range(0, 999_999_999)]
    public decimal UnitPrice { get; init; }

    [Range(0, 999_999_999)]
    public decimal Discount { get; init; }

    [Range(0, 100)]
    public decimal TaxRate { get; init; }
}

/// <summary>
/// Update payload. The invoice number, totals and ownership are never taken from the client:
/// the number is immutable and the totals are recomputed from the submitted lines.
/// </summary>
public record SaveInvoiceRequest
{
    [Required]
    public DateOnly InvoiceDate { get; init; }

    [Required]
    public DateOnly DueDate { get; init; }

    /// <summary>One of: Draft, Sent, PartiallyPaid, Paid, Overdue, Cancelled.</summary>
    public string? Status { get; init; }

    [MaxLength(2000)] public string? Notes { get; init; }
    [MaxLength(4000)] public string? Terms { get; init; }

    /// <summary>Optional. Only accepted while the invoice is still a draft.</summary>
    public List<SaveInvoiceItemRequest>? Items { get; init; }
}

/// <summary>
/// Direct invoice creation (V2.4): billing a customer without quoting them first. The result is an
/// ordinary invoice — same entity, same numbering, same lifecycle, same payment flow.
///
/// As with every other money-bearing payload, nothing financial is accepted from the client beyond
/// the line inputs: the number is allocated server-side and the totals are computed from the lines.
/// </summary>
public record CreateInvoiceRequest
{
    /// <summary>Must be one of the caller's own customers; anything else is a 404.</summary>
    [Required]
    public Guid CustomerId { get; init; }

    [Required]
    public DateOnly InvoiceDate { get; init; }

    /// <summary>Optional. Defaults to the invoice date plus the standard payment term.</summary>
    public DateOnly? DueDate { get; init; }

    [MaxLength(2000)] public string? Notes { get; init; }
    [MaxLength(4000)] public string? Terms { get; init; }

    [Required, MinLength(1)]
    public List<SaveInvoiceItemRequest> Items { get; init; } = new();
}

// ---- receivables (V2.5) ----------------------------------------------

/// <summary>
/// What the business is owed right now, plus the handful of invoices worth chasing first.
/// Every figure is aggregated by the database across all of the tenant's issued invoices.
/// </summary>
public record ReceivablesDto
{
    /// <summary>Across every issued invoice with a balance. Excludes drafts and cancellations.</summary>
    public decimal TotalOutstanding { get; init; }

    /// <summary>The part of the outstanding total that is already past its due date.</summary>
    public decimal TotalOverdue { get; init; }

    public int CountOutstanding { get; init; }
    public int CountOverdue { get; init; }

    public string Currency { get; init; } = "INR";

    /// <summary>Unpaid invoices, oldest due date first — what to chase today.</summary>
    public IReadOnlyList<ReceivableInvoiceDto> NeedsAttention { get; init; } = Array.Empty<ReceivableInvoiceDto>();
}

public record ReceivableInvoiceDto
{
    public Guid Id { get; init; }
    public string InvoiceNumber { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
    public DateOnly DueDate { get; init; }
    public decimal Outstanding { get; init; }
    public string Currency { get; init; } = "INR";
    public bool IsOverdue { get; init; }
}

/// <summary>
/// One customer's financial standing, and their invoices. Answers "does this customer owe me
/// anything?" without the owner adding up invoices by hand.
/// </summary>
public record CustomerSummaryDto
{
    public Guid CustomerId { get; init; }
    public string CustomerName { get; init; } = string.Empty;

    /// <summary>Total of every issued invoice — drafts and cancellations excluded throughout.</summary>
    public decimal TotalInvoiced { get; init; }
    public decimal TotalPaid { get; init; }
    public decimal TotalOutstanding { get; init; }
    public decimal TotalOverdue { get; init; }

    public int InvoiceCount { get; init; }
    public int OverdueCount { get; init; }
    public string Currency { get; init; } = "INR";

    /// <summary>
    /// This customer's invoices, paged server-side. The page the caller asked for, filtered in
    /// the database — not page one of every invoice filtered in the browser.
    /// </summary>
    public PagedResult<InvoiceListItemDto> Invoices { get; init; } =
        new(Array.Empty<InvoiceListItemDto>(), 1, 10, 0);
}
