namespace Quotely.Api.Models;

/// <summary>
/// A financial document demanding payment. Everything it prints is a snapshot taken when it was
/// raised, so later edits to the catalogue or the customer record cannot rewrite an issued invoice.
///
/// There is exactly one invoice entity, reached by two creation paths: conversion from an accepted
/// quotation (V2.2) and direct creation (V2.4). The path is recorded by nothing more than whether
/// <see cref="QuotationId"/> is set — a direct invoice needs no separate type, table or origin column.
/// </summary>
public class Invoice
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>
    /// The accepted quotation this invoice was raised from, or null when the owner billed the
    /// customer directly. Still one invoice per quotation: the unique index is filtered to
    /// non-null values, so any number of direct invoices may coexist.
    /// </summary>
    public Guid? QuotationId { get; set; }
    public Quotation? Quotation { get; set; }

    /// <summary>Kept for navigation and filtering only — never as the source of printed details.</summary>
    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }

    /// <summary>
    /// What kind of document this is. Standard is 0, so every invoice raised before import/export
    /// existed reads as exactly what it always was without a single row being touched.
    /// </summary>
    public InvoiceType Type { get; set; } = InvoiceType.Standard;

    /// <summary>
    /// The import/export detail, present only on a trade document. One invoice entity, one extra
    /// table — not a second kind of invoice with its own totals, payments and sharing.
    /// </summary>
    public TradeInvoiceDetails? TradeDetails { get; set; }

    /// <summary>Human-readable number, unique per user. Example: INV-000001.</summary>
    public string InvoiceNumber { get; set; } = string.Empty;
    /// <summary>Numeric part of the invoice number, used to allocate the next value.</summary>
    public int Sequence { get; set; }

    public DateOnly InvoiceDate { get; set; }
    public DateOnly DueDate { get; set; }

    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;

    // ---- customer snapshot ---------------------------------------------
    // The quotation reads the live Customer row, which is fine for an offer. An invoice must stay
    // historically true, so the billing details are copied in at conversion time.
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerCompanyName { get; set; }
    public string? CustomerEmail { get; set; }
    public string? CustomerPhone { get; set; }
    public string? CustomerAddressLine { get; set; }
    public string? CustomerCity { get; set; }
    public string? CustomerState { get; set; }
    public string? CustomerPostalCode { get; set; }
    public string? CustomerCountry { get; set; }

    // ---- money -----------------------------------------------------------
    // Server-computed from the snapshotted items. Never trusted from the client.
    public decimal Subtotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal GrandTotal { get; set; }
    /// <summary>Currency the invoice was raised in, so changing the profile later cannot restate it.</summary>
    public string Currency { get; set; } = "INR";

    public string? Notes { get; set; }
    public string? Terms { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // ---- customer-facing payment link (V2.3) ---------------------------
    // Identical model to the quotation share link: only the SHA-256 hash of the token is
    // persisted, so the raw URL exists exactly once — in the response that creates it.
    public string? PublicTokenHash { get; set; }
    public DateTime? PublicLinkCreatedAt { get; set; }

    public ICollection<InvoiceItem> Items { get; set; } = new List<InvoiceItem>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();

    /// <summary>
    /// Money is settled or written off: nothing about this invoice may change any more.
    /// V2.3 will extend this to any invoice carrying payments.
    /// </summary>
    public bool IsLocked => Status is InvoiceStatus.Paid or InvoiceStatus.Cancelled;

    /// <summary>
    /// Line items and totals may only be rewritten while the invoice is still a draft. Once it has
    /// gone out, the figures the customer received are history.
    /// </summary>
    public bool AllowsFinancialEdits => Status == InvoiceStatus.Draft;

    /// <summary>Raised directly rather than converted from a quotation. Derived, never stored.</summary>
    public bool IsDirect => QuotationId is null;

    /// <summary>Display hint only — the stored status stays authoritative.</summary>
    public bool IsOverdue(DateOnly today) => !IsLocked && DueDate < today;

    /// <summary>
    /// Whether a customer holding the public link may start a payment. A draft has not been
    /// issued, a cancelled invoice is void, and a settled one has nothing left to collect.
    /// The outstanding balance is checked separately, against recorded payments.
    /// </summary>
    ///
    /// A proforma invoice is refused regardless of status. It describes a shipment that has not
    /// happened and demands nothing, so a Pay button on one would invite a customer to pay against
    /// a document their bank will not recognise. That check only ever WITHHOLDS payment: a
    /// commercial invoice still has to satisfy every rule a domestic invoice does.
    public bool AcceptsPayments =>
        (TradeDetails?.DefaultPayable ?? true)
        && Status is InvoiceStatus.Sent or InvoiceStatus.PartiallyPaid or InvoiceStatus.Overdue;

    /// <summary>An import/export document. Derived from the discriminator, never stored twice.</summary>
    public bool IsTradeDocument => Type == InvoiceType.ImportExport;
}
