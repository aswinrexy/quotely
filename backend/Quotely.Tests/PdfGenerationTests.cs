using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Quotely.Api.DTOs;
using Quotely.Api.Models;
using Quotely.Api.Services;
using Xunit;

namespace Quotely.Tests;

public class PdfGenerationTests : IClassFixture<QuotelyApiFactory>
{
    private readonly QuotelyApiFactory _factory;

    public PdfGenerationTests(QuotelyApiFactory factory) => _factory = factory;

    private static Quotation SampleQuotation(int itemCount = 2) =>
        BuildQuotation(itemCount, description: "Split AC installation including mounting and gas charging.");

    private static Quotation BuildQuotation(int itemCount, string description)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var quotation = new Quotation
        {
            Id = Guid.NewGuid(),
            QuotationNumber = "QT-000001",
            QuotationDate = today,
            ValidUntil = today.AddDays(15),
            Notes = "Thank you for your business.",
            Terms = "Quotation is valid until the specified date.",
            Customer = new Customer
            {
                Name = "John Smith",
                CompanyName = "John Smith Construction",
                Email = "john@example.com",
                Phone = "+91 91234 56780",
                AddressLine = "42 Beach Road",
                City = "Chennai", State = "Tamil Nadu", PostalCode = "600006", Country = "India"
            }
        };

        for (var i = 0; i < itemCount; i++)
        {
            quotation.Items.Add(new QuotationItem
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

        QuotationCalculator.ApplyTotals(quotation);
        return quotation;
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
        var result = NewService().Generate(SampleQuotation(), SampleBusiness());

        result.Content.Should().NotBeEmpty();
        Encoding.ASCII.GetString(result.Content, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public void Long_documents_flow_onto_multiple_pages()
    {
        var single = NewService().Generate(SampleQuotation(itemCount: 2), SampleBusiness());
        var long_ = NewService().Generate(SampleQuotation(itemCount: 60), SampleBusiness());

        long_.Content.Length.Should().BeGreaterThan(single.Content.Length);
        PageCount(long_.Content).Should().BeGreaterThan(1);
    }

    [Fact]
    public void Very_long_item_descriptions_do_not_break_generation()
    {
        var quotation = BuildQuotation(3, description: string.Join(" ", Enumerable.Repeat("extremely detailed scope of work", 60)));

        var act = () => NewService().Generate(quotation, SampleBusiness());

        act.Should().NotThrow();
    }

    [Fact]
    public void A_missing_business_profile_still_produces_a_pdf()
    {
        var result = NewService().Generate(SampleQuotation(), business: null);

        Encoding.ASCII.GetString(result.Content, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public void File_name_combines_the_quotation_number_and_customer()
    {
        PdfService.BuildFileName(SampleQuotation()).Should().Be("QT-000001-John-Smith.pdf");
    }

    [Fact]
    public async Task The_pdf_endpoint_returns_a_downloadable_document()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var customer = await client.PostAsJsonAsync("/api/customers", new { name = "Priya Raman" })
            .ContinueWith(t => t.Result.Content.ReadFromJsonAsync<CustomerDto>()).Unwrap();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var quotation = await (await client.PostAsJsonAsync("/api/quotations", new
        {
            customerId = customer!.Id,
            quotationDate = today.ToString("yyyy-MM-dd"),
            validUntil = today.AddDays(10).ToString("yyyy-MM-dd"),
            items = new object[]
            {
                new { name = "Electrical Wiring", unit = "Hour", quantity = 4, unitPrice = 800, discount = 0, taxRate = 18 }
            }
        })).Content.ReadFromJsonAsync<QuotationDto>();

        var response = await client.PostAsync($"/api/quotations/{quotation!.Id}/pdf", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        response.Content.Headers.ContentDisposition!.FileName.Should().Contain("QT-000001-Priya-Raman");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
    }

    private static int PageCount(byte[] pdf)
    {
        var text = Encoding.Latin1.GetString(pdf);
        return System.Text.RegularExpressions.Regex.Matches(text, @"/Type\s*/Page[^s]").Count;
    }
}
