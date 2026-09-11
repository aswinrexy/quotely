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
    /// <summary>Past its due date and neither paid nor cancelled. Display hint only.</summary>
    public bool IsOverdue { get; init; }
    public string QuotationNumber { get; init; } = string.Empty;
}

public record InvoiceDto
{
    public Guid Id { get; init; }
    public string InvoiceNumber { get; init; } = string.Empty;

    /// <summary>The quotation this invoice came from, so the owner can navigate back.</summary>
    public Guid QuotationId { get; init; }
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
