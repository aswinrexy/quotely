using FluentAssertions;
using Quotely.Api.Models;
using Quotely.Api.Services;
using Xunit;

namespace Quotely.Tests;

public class InvoiceCalculatorTests
{
    private static InvoiceItem Item(decimal quantity, decimal unitPrice, decimal discount, decimal taxRate) =>
        new() { Name = "Line", Quantity = quantity, UnitPrice = unitPrice, Discount = discount, TaxRate = taxRate };

    [Fact]
    public void A_line_is_taxed_on_the_amount_left_after_discount()
    {
        var item = Item(2, 5000m, 500m, 18m);

        InvoiceCalculator.ApplyLine(item);

        item.LineSubtotal.Should().Be(10000m);
        item.LineTax.Should().Be(1710m);
        item.LineTotal.Should().Be(11210m);
    }

    [Fact]
    public void A_discount_larger_than_the_line_is_clamped_rather_than_going_negative()
    {
        var item = Item(1, 1000m, 5000m, 18m);

        InvoiceCalculator.ApplyLine(item);

        item.Discount.Should().Be(1000m);
        item.LineTotal.Should().Be(0m);
    }

    [Fact]
    public void Rounding_is_half_away_from_zero_at_two_decimal_places()
    {
        var item = Item(3, 33.335m, 0m, 0m);

        InvoiceCalculator.ApplyLine(item);

        item.LineSubtotal.Should().Be(100.01m);
    }

    [Fact]
    public void Invoice_totals_are_the_sum_of_the_lines()
    {
        var invoice = new Invoice();
        var items = new[] { Item(2, 5000m, 500m, 18m), Item(1, 2000m, 0m, 5m) };

        InvoiceCalculator.ApplyTotals(invoice, items);

        invoice.Subtotal.Should().Be(12000m);
        invoice.DiscountTotal.Should().Be(500m);
        invoice.TaxTotal.Should().Be(1810m);
        invoice.GrandTotal.Should().Be(13310m);
    }

    [Fact]
    public void The_invoice_rules_reproduce_the_quotation_rules_exactly()
    {
        // This equivalence is what lets a conversion recompute its own totals and still match the
        // accepted quotation. If the two calculators ever diverge, this test fails first.
        var quotation = new Quotation();
        var quotationItems = new[]
        {
            new QuotationItem { Quantity = 2.5m, UnitPrice = 1999.99m, Discount = 123.45m, TaxRate = 12.5m },
            new QuotationItem { Quantity = 1, UnitPrice = 0.05m, Discount = 0m, TaxRate = 28m }
        };
        QuotationCalculator.ApplyTotals(quotation, quotationItems);

        var invoice = new Invoice();
        var invoiceItems = quotationItems.Select(i => new InvoiceItem
        {
            Quantity = i.Quantity, UnitPrice = i.UnitPrice, Discount = i.Discount, TaxRate = i.TaxRate
        }).ToArray();
        InvoiceCalculator.ApplyTotals(invoice, invoiceItems);

        invoice.Subtotal.Should().Be(quotation.Subtotal);
        invoice.DiscountTotal.Should().Be(quotation.DiscountTotal);
        invoice.TaxTotal.Should().Be(quotation.TaxTotal);
        invoice.GrandTotal.Should().Be(quotation.GrandTotal);
    }
}
