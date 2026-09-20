using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Quotely.Api.DTOs;
using Xunit;

namespace Quotely.Tests;

/// <summary>
/// A business must have something to sell before it can bill for it.
///
/// Quotely used to accept a document whose every line was typed from scratch, which meant an
/// account could invoice for months with an empty catalogue: prices drifted line by line, and the
/// same service went out at three different amounts because nothing held the number in one place.
///
/// These tests cover the server half of the rule — the forms enforce the per-line requirement,
/// which the server cannot, because an InvoiceItem carries no ProductId by design. What is
/// asserted here is the part that IS enforceable and that a hand-rolled HTTP request could
/// otherwise walk straight past.
/// </summary>
public class CatalogueRequiredTests : IClassFixture<QuotelyApiFactory>
{
    private readonly QuotelyApiFactory _factory;

    public CatalogueRequiredTests(QuotelyApiFactory factory) => _factory = factory;

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    // ---- fixtures -------------------------------------------------------

    private static async Task<CustomerDto> CreateCustomerAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/customers", new
        {
            name = "Priya Nair",
            email = "priya@example.com",
            phone = "+91 98470 11223",
            addressLine = "12 Marine Drive",
            city = "Kochi",
            state = "Kerala",
            postalCode = "682031",
            country = "India"
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<CustomerDto>())!;
    }

    private static Task<HttpResponseMessage> CreateProductAsync(HttpClient client) =>
        client.PostAsJsonAsync("/api/products", new
        {
            name = "Rewiring",
            unit = "Service",
            price = 5000m,
            taxRate = 18m
        });

    private static Task<HttpResponseMessage> PostInvoiceAsync(HttpClient client, Guid customerId) =>
        client.PostAsJsonAsync("/api/invoices", new
        {
            customerId,
            invoiceDate = Today.ToString("yyyy-MM-dd"),
            items = new[]
            {
                new { name = "Rewiring", unit = "Service", quantity = 1, unitPrice = 5000, discount = 0, taxRate = 18 }
            }
        });

    private static Task<HttpResponseMessage> PostQuotationAsync(HttpClient client, Guid customerId) =>
        client.PostAsJsonAsync("/api/quotations", new
        {
            customerId,
            quotationDate = Today.ToString("yyyy-MM-dd"),
            validUntil = Today.AddDays(15).ToString("yyyy-MM-dd"),
            status = "Draft",
            items = new[]
            {
                new { name = "Rewiring", unit = "Service", quantity = 1, unitPrice = 5000, discount = 0, taxRate = 18 }
            }
        });

    // ---- the rule -------------------------------------------------------

    [Fact]
    public async Task An_invoice_cannot_be_raised_while_the_catalogue_is_empty()
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false, seedCatalogue: false);
        var customer = await CreateCustomerAsync(client);

        var response = await PostInvoiceAsync(client, customer.Id);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        // The message has to name the fix, not the rule. Someone reading it should know to go and
        // add a product, without already understanding why.
        body.Should().Contain("product or service");
    }

    [Fact]
    public async Task A_quotation_cannot_be_raised_while_the_catalogue_is_empty()
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false, seedCatalogue: false);
        var customer = await CreateCustomerAsync(client);

        var response = await PostQuotationAsync(client, customer.Id);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("product or service");
    }

    [Fact]
    public async Task Adding_one_product_is_enough_to_unblock_both_documents()
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false, seedCatalogue: false);
        var customer = await CreateCustomerAsync(client);

        (await PostInvoiceAsync(client, customer.Id)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await CreateProductAsync(client)).StatusCode.Should().Be(HttpStatusCode.Created);

        // One entry, not a matching one: the guard is about the catalogue existing, and the line
        // itself is still a free-standing snapshot. Asserting more than that here would be
        // asserting a rule the server does not — and cannot — enforce.
        (await PostInvoiceAsync(client, customer.Id)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await PostQuotationAsync(client, customer.Id)).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Another_businesss_catalogue_does_not_count()
    {
        // The obvious way to get this wrong is a global Products.Any(). One busy tenant would then
        // silently unblock every empty account on the instance.
        var stocked = await _factory.CreateSignedInClientAsync(connectPayments: false);
        (await CreateProductAsync(stocked)).StatusCode.Should().Be(HttpStatusCode.Created);

        var empty = await _factory.CreateSignedInClientAsync(connectPayments: false, seedCatalogue: false);
        var customer = await CreateCustomerAsync(empty);

        (await PostInvoiceAsync(empty, customer.Id)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PostQuotationAsync(empty, customer.Id)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Deleting_the_last_product_does_not_disturb_documents_already_raised()
    {
        // The guard stops the NEXT document, never what exists — the same shape as every free-tier
        // limit. An invoice already sent to a customer is not ours to invalidate.
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false, seedCatalogue: false);
        var customer = await CreateCustomerAsync(client);

        var product = (await (await CreateProductAsync(client)).Content.ReadFromJsonAsync<ProductDto>())!;
        var invoice = (await (await PostInvoiceAsync(client, customer.Id))
            .Content.ReadFromJsonAsync<InvoiceDto>())!;

        (await client.DeleteAsync($"/api/products/{product.Id}")).IsSuccessStatusCode.Should().BeTrue();

        var reread = await client.GetAsync($"/api/invoices/{invoice.Id}");
        reread.StatusCode.Should().Be(HttpStatusCode.OK);
        (await reread.Content.ReadFromJsonAsync<InvoiceDto>())!.Items.Should().HaveCount(1);

        // But the next one is refused, because the catalogue is empty again.
        (await PostInvoiceAsync(client, customer.Id)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
