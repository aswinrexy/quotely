using Quotely.Api.Models;

namespace Quotely.Api.Services;

/// <summary>
/// Single source of truth for quotation money maths. The frontend mirrors these rules for
/// live feedback, but every persisted or printed value comes from here.
///
/// Per line:  gross = qty * unitPrice
///            net   = gross - discount          (discount is an absolute amount, clamped to gross)
///            tax   = net * taxRate / 100
///            total = net + tax
/// Per quotation: subtotal = SUM(gross), discountTotal = SUM(discount),
///                taxTotal = SUM(tax), grandTotal = subtotal - discountTotal + taxTotal
/// </summary>
public static class QuotationCalculator
{
    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public static void ApplyLine(QuotationItem item)
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

    public static void ApplyTotals(Quotation quotation) => ApplyTotals(quotation, quotation.Items);

    /// <summary>Totals a quotation from an explicit item list, for flows that rebuild the lines.</summary>
    public static void ApplyTotals(Quotation quotation, IEnumerable<QuotationItem> items)
    {
        decimal subtotal = 0, discount = 0, tax = 0;

        foreach (var item in items)
        {
            ApplyLine(item);
            subtotal += item.LineSubtotal;
            discount += item.Discount;
            tax += item.LineTax;
        }

        quotation.Subtotal = Round(subtotal);
        quotation.DiscountTotal = Round(discount);
        quotation.TaxTotal = Round(tax);
        quotation.GrandTotal = Round(quotation.Subtotal - quotation.DiscountTotal + quotation.TaxTotal);
    }
}
