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

    // ---- customer-facing share link (V2.1) ----------------------------
    // Only the SHA-256 hash of the public token is persisted; the raw token exists once,
    // in the response that creates the link. Regenerating replaces the hash, which
    // invalidates the previous link.
    public string? PublicTokenHash { get; set; }
    public DateTime? PublicLinkCreatedAt { get; set; }

    // ---- customer response captured through the public link ------------
    // Kept on the quotation rather than written back onto the Customer record:
    // the person who responds is not necessarily the stored contact.
    public DateTime? RespondedAt { get; set; }
    public string? RespondedByName { get; set; }
    public string? RespondedByEmail { get; set; }
    public string? ResponseComment { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<QuotationItem> Items { get; set; } = new List<QuotationItem>();

    /// <summary>A quotation past its valid-until date is closed to customer responses.</summary>
    public bool IsExpired(DateOnly today) => ValidUntil < today;

    /// <summary>Accepted and Rejected are terminal: the public page cannot change them again.</summary>
    public bool HasResponded => Status is QuotationStatus.Accepted or QuotationStatus.Rejected;
}
