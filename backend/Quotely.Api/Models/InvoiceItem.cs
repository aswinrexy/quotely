namespace Quotely.Api.Models;

/// <summary>
/// A frozen copy of one quotation line. There is deliberately no ProductId here: an invoice line
/// must never resolve its description or price through the live catalogue.
/// </summary>
public class InvoiceItem
{
    public Guid Id { get; set; }
    public Guid InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }

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
