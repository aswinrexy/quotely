namespace Quotely.Api.Models;

public class QuotationItem
{
    public Guid Id { get; set; }
    public Guid QuotationId { get; set; }
    public Quotation? Quotation { get; set; }

    /// <summary>Optional link to the catalogue. Items are snapshotted so later product edits don't rewrite history.</summary>
    public Guid? ProductId { get; set; }
    public Product? Product { get; set; }

    public int SortOrder { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Unit { get; set; } = "Service";
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    /// <summary>Absolute discount amount applied to this line (before tax).</summary>
    public decimal Discount { get; set; }
    public decimal TaxRate { get; set; }

    // Server-computed.
    public decimal LineSubtotal { get; set; }
    public decimal LineTax { get; set; }
    public decimal LineTotal { get; set; }
}
