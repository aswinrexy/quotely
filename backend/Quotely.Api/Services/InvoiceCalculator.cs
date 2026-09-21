using Quotely.Api.Models;

namespace Quotely.Api.Services;

/// <summary>
/// Invoice money maths. The rules are identical to <see cref="QuotationCalculator"/> — which is
/// why converting an accepted quotation reproduces its totals exactly from the snapshotted lines —
/// but they are applied to invoice entities, so the two documents can never share state.
///
/// Per line:  gross = qty * unitPrice
///            net   = gross - discount          (discount is an absolute amount, clamped to gross)
///            tax   = net * taxRate / 100
///            total = net + tax
/// Per invoice: subtotal = SUM(gross), discountTotal = SUM(discount),
///              taxTotal = SUM(tax), grandTotal = subtotal - discountTotal + taxTotal
/// </summary>
public static class InvoiceCalculator
{
    public static decimal Round(decimal value) => QuotationCalculator.Round(value);

    /// <summary>
    /// The figure the rate is multiplied by.
    ///
    /// For every domestic line this is simply the quantity. A trade line may instead be priced by
    /// weight, and the distinction is not cosmetic: the reference proforma heads its rate column
    /// "Rate/Kg" while multiplying by the package count, so a calculator that trusted the label
    /// would turn 132 boxes at 590 into 257,004 instead of 77,880. The basis is stored on the line
    /// precisely so this decision is never inferred from a piece of text.
    /// </summary>
    private static decimal PricedQuantity(InvoiceItem item) =>
        item.TradeDetails?.RateBasis == TradeRateBasis.PerNetWeight
            ? item.TradeDetails.NetWeight ?? 0m
            : item.Quantity;

    public static void ApplyLine(InvoiceItem item)
    {
        var gross = Round(PricedQuantity(item) * item.UnitPrice);
        var discount = Round(Math.Clamp(item.Discount, 0m, gross));
        var net = gross - discount;
        var tax = Round(net * item.TaxRate / 100m);

        item.Discount = discount;
        item.LineSubtotal = gross;
        item.LineTax = tax;
        item.LineTotal = Round(net + tax);
    }

    /// <summary>Totals an invoice from an explicit item list, for flows that rebuild the lines.</summary>
    public static void ApplyTotals(Invoice invoice, IEnumerable<InvoiceItem> items)
    {
        decimal subtotal = 0, discount = 0, tax = 0;

        foreach (var item in items)
        {
            ApplyLine(item);
            subtotal += item.LineSubtotal;
            discount += item.Discount;
            tax += item.LineTax;
        }

        invoice.Subtotal = Round(subtotal);
        invoice.DiscountTotal = Round(discount);
        invoice.TaxTotal = Round(tax);
        invoice.GrandTotal = Round(invoice.Subtotal - invoice.DiscountTotal + invoice.TaxTotal);
    }

    public static void ApplyTotals(Invoice invoice) => ApplyTotals(invoice, invoice.Items);
}
