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

    public static void ApplyLine(InvoiceItem item)
    {
        var gross = Round(item.Quantity * item.UnitPrice);
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
