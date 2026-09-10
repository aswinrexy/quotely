using FluentAssertions;
using Quotely.Api.Models;
using Quotely.Api.Services;
using Xunit;

namespace Quotely.Tests;

public class QuotationCalculatorTests
{
    private static QuotationItem Item(decimal qty, decimal price, decimal discount = 0, decimal taxRate = 0) =>
        new() { Name = "Line", Quantity = qty, UnitPrice = price, Discount = discount, TaxRate = taxRate };

    [Fact]
    public void Line_total_adds_tax_on_top_of_the_net_amount()
    {
        var item = Item(qty: 2, price: 5000m, taxRate: 18m);

        QuotationCalculator.ApplyLine(item);

        item.LineSubtotal.Should().Be(10000m);
        item.LineTax.Should().Be(1800m);
        item.LineTotal.Should().Be(11800m);
    }

    [Fact]
    public void Discount_reduces_the_taxable_amount()
    {
        var item = Item(qty: 2, price: 5000m, discount: 500m, taxRate: 18m);

        QuotationCalculator.ApplyLine(item);

        item.LineSubtotal.Should().Be(10000m);
        item.LineTax.Should().Be(1710m);   // 18% of 9,500 - not of 10,000
        item.LineTotal.Should().Be(11210m);
    }

    [Fact]
    public void Discount_larger_than_the_line_is_clamped_so_totals_never_go_negative()
    {
        var item = Item(qty: 1, price: 1000m, discount: 5000m, taxRate: 18m);

        QuotationCalculator.ApplyLine(item);

        item.Discount.Should().Be(1000m);
        item.LineTax.Should().Be(0m);
        item.LineTotal.Should().Be(0m);
    }

    [Fact]
    public void Zero_tax_rate_produces_no_tax()
    {
        var item = Item(qty: 3, price: 750m);

        QuotationCalculator.ApplyLine(item);

        item.LineTax.Should().Be(0m);
        item.LineTotal.Should().Be(2250m);
    }

    [Fact]
    public void Amounts_are_rounded_to_two_decimals_away_from_zero()
    {
        var item = Item(qty: 3, price: 33.335m, taxRate: 5m);

        QuotationCalculator.ApplyLine(item);

        item.LineSubtotal.Should().Be(100.01m);   // 100.005 rounds up
        item.LineTax.Should().Be(5.00m);
        item.LineTotal.Should().Be(105.01m);
    }

    [Fact]
    public void Quotation_totals_match_the_worked_example()
    {
        var quotation = new Quotation();
        quotation.Items.Add(Item(qty: 2, price: 5000m, discount: 500m, taxRate: 18m));
        quotation.Items.Add(Item(qty: 1, price: 1500m, taxRate: 18m));

        QuotationCalculator.ApplyTotals(quotation);

        quotation.Subtotal.Should().Be(11500m);
        quotation.DiscountTotal.Should().Be(500m);
        quotation.TaxTotal.Should().Be(1980m);
        quotation.GrandTotal.Should().Be(12980m);
    }

    [Fact]
    public void Grand_total_always_equals_subtotal_minus_discount_plus_tax()
    {
        var quotation = new Quotation();
        quotation.Items.Add(Item(qty: 7, price: 199.99m, discount: 49.5m, taxRate: 12.5m));
        quotation.Items.Add(Item(qty: 1.5m, price: 800m, taxRate: 18m));
        quotation.Items.Add(Item(qty: 10, price: 12.35m, discount: 3m, taxRate: 5m));

        QuotationCalculator.ApplyTotals(quotation);

        quotation.GrandTotal.Should()
            .Be(quotation.Subtotal - quotation.DiscountTotal + quotation.TaxTotal);
        quotation.GrandTotal.Should().Be(quotation.Items.Sum(i => i.LineTotal));
    }

    [Fact]
    public void An_empty_quotation_totals_zero()
    {
        var quotation = new Quotation();

        QuotationCalculator.ApplyTotals(quotation);

        quotation.GrandTotal.Should().Be(0m);
    }
}
