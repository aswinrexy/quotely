namespace Quotely.Api.Models;

/// <summary>
/// Invoice state. Deliberately separate from <see cref="QuotationStatus"/>: a quotation is an
/// offer, an invoice is a demand for payment, and the two lifecycles do not overlap.
/// </summary>
public enum InvoiceStatus
{
    Draft = 0,
    Sent = 1,
    PartiallyPaid = 2,
    Paid = 3,
    Overdue = 4,
    Cancelled = 5
}
