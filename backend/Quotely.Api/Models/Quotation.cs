namespace Quotely.Api.Models;

public class Quotation
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public AppUser? User { get; set; }

    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }

    /// <summary>Human-readable number, unique per user. Example: QT-000001.</summary>
    public string QuotationNumber { get; set; } = string.Empty;
    /// <summary>Numeric part of the quotation number, used to allocate the next value.</summary>
    public int Sequence { get; set; }

    public DateOnly QuotationDate { get; set; }
    public DateOnly ValidUntil { get; set; }

    public string? Notes { get; set; }
    public string? Terms { get; set; }
    public QuotationStatus Status { get; set; } = QuotationStatus.Draft;

    // Server-computed totals. Never trusted from the client.
    public decimal Subtotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal GrandTotal { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<QuotationItem> Items { get; set; } = new List<QuotationItem>();
}
