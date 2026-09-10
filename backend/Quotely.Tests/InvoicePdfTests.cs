using System.Text;
using FluentAssertions;
using Quotely.Api.Models;
using Quotely.Api.Pdf;
using Quotely.Api.Services;
using Xunit;

namespace Quotely.Tests;

/// <summary>
/// Invoice PDF rendering. These build the document straight from an <see cref="Invoice"/> so the
/// snapshot — not the catalogue — is demonstrably the only input.
/// </summary>
public class InvoicePdfTests
{
    private static Invoice SampleInvoice(int itemCount = 2, string description = "Split AC installation.")
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            InvoiceNumber = "INV-000001",
            InvoiceDate = today,
            DueDate = today.AddDays(15),
            Status = InvoiceStatus.Sent,
            Currency = "INR",
            Notes = "Thank you for your business.",
            Terms = "Payment due within 15 days.",
            CustomerName = "John Smith",
            CustomerCompanyName = "John Smith Construction",
            CustomerEmail = "john@example.com",
            CustomerPhone = "+91 91234 56780",
            CustomerAddressLine = "42 Beach Road",
            CustomerCity = "Chennai",
            CustomerState = "Tamil Nadu",
            CustomerPostalCode = "600006",
            CustomerCountry = "India",
            Quotation = new Quotation { QuotationNumber = "QT-000001" }
        };

        for (var i = 0; i < itemCount; i++)
        {
            invoice.Items.Add(new InvoiceItem
            {
                SortOrder = i,
                Name = $"AC Installation {i + 1}",
                Description = description,
                Unit = "Service",
                Quantity = 2,
                UnitPrice = 5000m,
                Discount = 250m,
                TaxRate = 18m
            });
        }

        InvoiceCalculator.ApplyTotals(invoice);
        return invoice;
    }

    private static BusinessProfile SampleBusiness() => new()
    {
        BusinessName = "ABC Electricals",
        BusinessEmail = "hello@abcelectricals.example",
        Phone = "+91 98765 43210",
        AddressLine = "123 Main Street",
        City = "Chennai", State = "Tamil Nadu", PostalCode = "600040", Country = "India",
        TaxNumber = "33ABCDE1234F1Z5",
        Currency = "INR"
    };

    private static PdfService NewService() =>
        new(Microsoft.Extensions.Logging.Abstractions.NullLogger<PdfService>.Instance);

    [Fact]
    public void Generated_file_is_a_valid_pdf()
    {
        var result = NewService().GenerateInvoice(SampleInvoice(), SampleBusiness());

        result.Content.Should().NotBeEmpty();
        Encoding.ASCII.GetString(result.Content, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public void The_document_identifies_the_invoice_and_the_customer()
    {
        // QuestPDF draws text as glyphs, so the rendered bytes cannot be grepped. The document
        // metadata carries the same values the header prints and is readable.
        var metadata = new InvoiceDocument(SampleInvoice(), SampleBusiness()).GetMetadata();

        metadata.Title.Should().Be("Invoice INV-000001");
        metadata.Subject.Should().Be("Invoice for John Smith");
        metadata.Author.Should().Be("ABC Electricals");
    }

    [Fact]
    public void Totals_printed_on_the_invoice_come_from_its_own_snapshot()
    {
        var invoice = SampleInvoice();

        // 2 lines × (2 × 5,000 = 10,000 gross − 250 discount = 9,750 net + 18% tax 1,755).
        invoice.Subtotal.Should().Be(20000m);
        invoice.DiscountTotal.Should().Be(500m);
        invoice.TaxTotal.Should().Be(3510m);
        invoice.GrandTotal.Should().Be(23010m);

        var act = () => NewService().GenerateInvoice(invoice, SampleBusiness());
        act.Should().NotThrow();
    }

    [Fact]
    public void Long_invoices_flow_onto_multiple_pages()
    {
        var single = NewService().GenerateInvoice(SampleInvoice(itemCount: 2), SampleBusiness());
        var long_ = NewService().GenerateInvoice(SampleInvoice(itemCount: 60), SampleBusiness());

        long_.Content.Length.Should().BeGreaterThan(single.Content.Length);
        PageCount(long_.Content).Should().BeGreaterThan(1);
    }

    [Fact]
    public void Very_long_item_descriptions_do_not_break_generation()
    {
        var invoice = SampleInvoice(3, string.Join(" ", Enumerable.Repeat("extremely detailed scope of work", 60)));

        var act = () => NewService().GenerateInvoice(invoice, SampleBusiness());

        act.Should().NotThrow();
    }

    [Fact]
    public void A_missing_business_profile_still_produces_a_pdf()
    {
        var result = NewService().GenerateInvoice(SampleInvoice(), business: null);

        Encoding.ASCII.GetString(result.Content, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public void File_name_combines_the_invoice_number_and_the_snapshotted_customer()
    {
        PdfService.BuildFileName(SampleInvoice()).Should().Be("INV-000001-John-Smith.pdf");
    }

    private static int PageCount(byte[] pdf)
    {
        var text = Encoding.Latin1.GetString(pdf);
        return System.Text.RegularExpressions.Regex.Matches(text, @"/Type\s*/Page[^s]").Count;
    }
}
