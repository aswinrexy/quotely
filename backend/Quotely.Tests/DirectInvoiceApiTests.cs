using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Quotely.Api.DTOs;
using Quotely.Api.Models;
using Xunit;

namespace Quotely.Tests;

/// <summary>
/// V2.4 — raising an invoice without a quotation. The point of most of these tests is sameness:
/// a direct invoice must be indistinguishable from a converted one everywhere downstream, and the
/// quotation → invoice path must be exactly as it was.
/// </summary>
public class DirectInvoiceApiTests : IClassFixture<QuotelyApiFactory>
{
    private readonly QuotelyApiFactory _factory;

    public DirectInvoiceApiTests(QuotelyApiFactory factory) => _factory = factory;

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    // ---- fixtures -------------------------------------------------------

    private static async Task<CustomerDto> CreateCustomerAsync(HttpClient client, string name = "John Smith")
    {
        var response = await client.PostAsJsonAsync("/api/customers", new
        {
            name,
            companyName = $"{name} Ltd",
            email = "john@example.com",
            phone = "+91 91234 56780",
            addressLine = "42 Beach Road",
            city = "Chennai",
            state = "Tamil Nadu",
            postalCode = "600006",
            country = "India"
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<CustomerDto>())!;
    }

    private static object[] DefaultItems => new object[]
    {
        new { name = "Wiring", unit = "Service", quantity = 2, unitPrice = 5000, discount = 0, taxRate = 18 },
        new { name = "Call-out", unit = "Service", quantity = 1, unitPrice = 2000, discount = 0, taxRate = 18 }
    };

    private static object Payload(Guid customerId, object[]? items = null, string? dueDate = null,
        string? invoiceDate = null) => new
    {
        customerId,
        invoiceDate = invoiceDate ?? Today.ToString("yyyy-MM-dd"),
        dueDate,
        notes = "Thanks for your business.",
        terms = "Payment due within 15 days.",
        items = items ?? DefaultItems
    };

    private static async Task<InvoiceDto> CreateDirectAsync(
        HttpClient client, Guid customerId, object[]? items = null, string? dueDate = null)
    {
        var response = await client.PostAsJsonAsync("/api/invoices", Payload(customerId, items, dueDate));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<InvoiceDto>())!;
    }

    /// <summary>Mints the payment link and returns the whole response, share material included.</summary>
    private static async Task<PublicInvoiceLinkDto> CreateLinkAsync(HttpClient client, Guid invoiceId)
    {
        var response = await client.PostAsync($"/api/invoices/{invoiceId}/public-link", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<PublicInvoiceLinkDto>())!;
    }

    private static string TokenOf(PublicInvoiceLinkDto link) => link.Url[(link.Url.LastIndexOf('/') + 1)..];

    // ---- creation -------------------------------------------------------

    [Fact]
    public async Task An_owner_can_raise_an_invoice_without_a_quotation()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var customer = await CreateCustomerAsync(client);

        var invoice = await CreateDirectAsync(client, customer.Id);

        invoice.QuotationId.Should().BeNull("a direct invoice has no source quotation");
        invoice.QuotationNumber.Should().BeEmpty();
        invoice.Status.Should().Be("Draft", "sharing is what issues an invoice, not creating it");
        invoice.Items.Should().HaveCount(2);
        invoice.InvoiceNumber.Should().MatchRegex(@"^INV-\d{6}$");
    }

    [Fact]
    public async Task An_unauthenticated_request_cannot_raise_an_invoice()
    {
        var anonymous = _factory.CreateClient();

        var response = await anonymous.PostAsJsonAsync("/api/invoices", Payload(Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_invoice_cannot_be_raised_against_another_tenants_customer()
    {
        var owner = await _factory.CreateSignedInClientAsync();
        var customer = await CreateCustomerAsync(owner, "Owned Customer");

        var intruder = await _factory.CreateSignedInClientAsync();
        var response = await intruder.PostAsJsonAsync("/api/invoices", Payload(customer.Id));

        // 404 rather than 403: a rejected id must not confirm that the customer exists.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_unknown_customer_is_rejected()
    {
        var client = await _factory.CreateSignedInClientAsync();

        var response = await client.PostAsJsonAsync("/api/invoices", Payload(Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    // Quantity must be positive, price and discount non-negative, tax a percentage, name present.
    [InlineData(0, 100, 0, 18, "Wiring")]
    [InlineData(-1, 100, 0, 18, "Wiring")]
    [InlineData(1, -100, 0, 18, "Wiring")]
    [InlineData(1, 100, -5, 18, "Wiring")]
    [InlineData(1, 100, 0, 150, "Wiring")]
    [InlineData(1, 100, 0, 18, "   ")]
    public async Task Invalid_line_data_is_rejected(
        decimal quantity, decimal unitPrice, decimal discount, decimal taxRate, string name)
    {
        var client = await _factory.CreateSignedInClientAsync();
        var customer = await CreateCustomerAsync(client);

        var response = await client.PostAsJsonAsync("/api/invoices", Payload(customer.Id, new object[]
        {
            new { name, unit = "Service", quantity, unitPrice, discount, taxRate }
        }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_invoice_with_no_items_is_rejected()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var customer = await CreateCustomerAsync(client);

        var response = await client.PostAsJsonAsync("/api/invoices", Payload(customer.Id, Array.Empty<object>()));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_due_date_before_the_invoice_date_is_rejected()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var customer = await CreateCustomerAsync(client);

        var response = await client.PostAsJsonAsync("/api/invoices",
            Payload(customer.Id, dueDate: Today.AddDays(-1).ToString("yyyy-MM-dd")));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---- server-side money ----------------------------------------------

    [Fact]
    public async Task The_server_computes_the_totals_from_the_submitted_lines()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var customer = await CreateCustomerAsync(client);

        // 2 × 5000 = 10,000 less 500 = 9,500 + 18% = 11,210
        // 1 × 2000 = 2,000 + 18% = 2,360
        var invoice = await CreateDirectAsync(client, customer.Id, new object[]
        {
            new { name = "Wiring", unit = "Service", quantity = 2, unitPrice = 5000, discount = 500, taxRate = 18 },
            new { name = "Call-out", unit = "Service", quantity = 1, unitPrice = 2000, discount = 0, taxRate = 18 }
        });

        invoice.Subtotal.Should().Be(12000m);
        invoice.DiscountTotal.Should().Be(500m);
        invoice.TaxTotal.Should().Be(2070m);
        invoice.GrandTotal.Should().Be(13570m);
        invoice.Outstanding.Should().Be(13570m);
        invoice.Paid.Should().Be(0m);
    }

    [Fact]
    public async Task Totals_supplied_by_the_client_are_ignored()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var customer = await CreateCustomerAsync(client);

        // Extra fields the DTO does not define. If any were bound, the totals would not be 1,180.
        var response = await client.PostAsJsonAsync("/api/invoices", new
        {
            customerId = customer.Id,
            invoiceDate = Today.ToString("yyyy-MM-dd"),
            invoiceNumber = "INV-999999",
            sequence = 999,
            subtotal = 1m,
            grandTotal = 1m,
            status = "Paid",
            items = new object[]
            {
                new { name = "Wiring", unit = "Service", quantity = 1, unitPrice = 1000, discount = 0, taxRate = 18 }
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var invoice = (await response.Content.ReadFromJsonAsync<InvoiceDto>())!;

        invoice.GrandTotal.Should().Be(1180m);
        invoice.InvoiceNumber.Should().NotBe("INV-999999");
        invoice.Status.Should().Be("Draft", "a new invoice cannot be born paid");
    }

    [Fact]
    public async Task The_due_date_defaults_to_the_standard_payment_term()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var customer = await CreateCustomerAsync(client);

        var invoice = await CreateDirectAsync(client, customer.Id);

        invoice.DueDate.Should().Be(invoice.InvoiceDate.AddDays(15));
    }

    [Fact]
    public async Task The_billing_details_are_snapshotted_and_survive_a_later_customer_edit()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var customer = await CreateCustomerAsync(client, "Original Name");

        var invoice = await CreateDirectAsync(client, customer.Id);
        invoice.Customer.Name.Should().Be("Original Name");

        var renamed = await client.PutAsJsonAsync($"/api/customers/{customer.Id}", new
        {
            name = "Renamed Afterwards", email = "new@example.com", city = "Mumbai"
        });
        renamed.StatusCode.Should().Be(HttpStatusCode.OK);

        var reread = (await client.GetFromJsonAsync<InvoiceDto>($"/api/invoices/{invoice.Id}"))!;
        reread.Customer.Name.Should().Be("Original Name", "an issued invoice is history");
        reread.Customer.Email.Should().Be("john@example.com");
    }

    // ---- numbering ------------------------------------------------------

    [Fact]
    public async Task Both_creation_paths_share_one_numbering_sequence()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var customer = await CreateCustomerAsync(client);

        var first = await CreateDirectAsync(client, customer.Id);

        // A converted invoice must continue the same run, not start a second scheme.
        var quotation = (await (await client.PostAsJsonAsync("/api/quotations", new
        {
            customerId = customer.Id,
            quotationDate = Today.ToString("yyyy-MM-dd"),
            validUntil = Today.AddDays(15).ToString("yyyy-MM-dd"),
            status = "Accepted",
            items = new object[]
            {
                new { name = "Wiring", unit = "Service", quantity = 1, unitPrice = 1000, discount = 0, taxRate = 0 }
            }
        })).Content.ReadFromJsonAsync<QuotationDto>())!;

        var convertResponse = await client.PostAsync($"/api/quotations/{quotation.Id}/convert-to-invoice", null);
        convertResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var converted = (await convertResponse.Content.ReadFromJsonAsync<InvoiceDto>())!;

        var third = await CreateDirectAsync(client, customer.Id);

        first.InvoiceNumber.Should().Be("INV-000001");
        converted.InvoiceNumber.Should().Be("INV-000002");
        third.InvoiceNumber.Should().Be("INV-000003");
    }

    [Fact]
    public async Task Numbering_restarts_per_tenant()
    {
        var first = await _factory.CreateSignedInClientAsync();
        var firstInvoice = await CreateDirectAsync(first, (await CreateCustomerAsync(first)).Id);

        var second = await _factory.CreateSignedInClientAsync();
        var secondInvoice = await CreateDirectAsync(second, (await CreateCustomerAsync(second)).Id);

        firstInvoice.InvoiceNumber.Should().Be("INV-000001");
        secondInvoice.InvoiceNumber.Should().Be("INV-000001");
    }

    [Fact]
    public async Task Many_direct_invoices_can_coexist_despite_the_one_invoice_per_quotation_rule()
    {
        // The unique index on QuotationId is filtered to non-null values. If it were not, the
        // second direct invoice here would fail to insert — they all carry NULL.
        var client = await _factory.CreateSignedInClientAsync();
        var customer = await CreateCustomerAsync(client);

        var numbers = new List<string>();
        for (var i = 0; i < 3; i++)
            numbers.Add((await CreateDirectAsync(client, customer.Id)).InvoiceNumber);

        numbers.Should().OnlyHaveUniqueItems().And.HaveCount(3);
    }

    // ---- it is an ordinary invoice --------------------------------------

    [Fact]
    public async Task A_direct_invoice_is_reachable_through_the_normal_invoice_apis()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var customer = await CreateCustomerAsync(client);
        var invoice = await CreateDirectAsync(client, customer.Id);

        var fetched = (await client.GetFromJsonAsync<InvoiceDto>($"/api/invoices/{invoice.Id}"))!;
        fetched.Id.Should().Be(invoice.Id);

        var list = (await client.GetFromJsonAsync<PagedResult<InvoiceListItemDto>>("/api/invoices"))!;
        list.Items.Should().ContainSingle(i => i.Id == invoice.Id)
            .Which.QuotationNumber.Should().BeEmpty();

        // Searching by invoice number must still work with no quotation to join to.
        var searched = (await client.GetFromJsonAsync<PagedResult<InvoiceListItemDto>>(
            $"/api/invoices?search={invoice.InvoiceNumber}"))!;
        searched.Items.Should().ContainSingle(i => i.Id == invoice.Id);

        var pdf = await client.GetAsync($"/api/invoices/{invoice.Id}/pdf");
        pdf.StatusCode.Should().Be(HttpStatusCode.OK);
        pdf.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        (await pdf.Content.ReadAsByteArrayAsync()).Length.Should().BeGreaterThan(1000);
    }

    [Fact]
    public async Task A_direct_invoice_follows_the_ordinary_edit_rules()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var customer = await CreateCustomerAsync(client);
        var invoice = await CreateDirectAsync(client, customer.Id);

        invoice.CanEdit.Should().BeTrue();
        invoice.CanEditItems.Should().BeTrue("it is still a draft");

        var updated = await client.PutAsJsonAsync($"/api/invoices/{invoice.Id}", new
        {
            invoiceDate = invoice.InvoiceDate.ToString("yyyy-MM-dd"),
            dueDate = invoice.DueDate.AddDays(5).ToString("yyyy-MM-dd"),
            status = "Sent",
            notes = "Updated note"
        });
        updated.StatusCode.Should().Be(HttpStatusCode.OK);

        var issued = (await updated.Content.ReadFromJsonAsync<InvoiceDto>())!;
        issued.Status.Should().Be("Sent");
        issued.CanEditItems.Should().BeFalse("an issued invoice keeps the figures the customer saw");
    }

    [Fact]
    public async Task One_tenant_cannot_read_or_change_another_tenants_direct_invoice()
    {
        var owner = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateDirectAsync(owner, (await CreateCustomerAsync(owner)).Id);

        var intruder = await _factory.CreateSignedInClientAsync();

        (await intruder.GetAsync($"/api/invoices/{invoice.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await intruder.PostAsync($"/api/invoices/{invoice.Id}/public-link", null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await intruder.DeleteAsync($"/api/invoices/{invoice.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await intruder.GetFromJsonAsync<PagedResult<InvoiceListItemDto>>("/api/invoices"))!
            .Items.Should().BeEmpty();
    }

    // ---- sharing and payment --------------------------------------------

    [Fact]
    public async Task A_direct_invoice_uses_the_same_public_link_mechanism()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var customer = await CreateCustomerAsync(client);
        var invoice = await CreateDirectAsync(client, customer.Id);

        var link = await CreateLinkAsync(client, invoice.Id);
        var token = TokenOf(link);

        token.Should().HaveLength(43, "the same 32-byte token the quotation link uses");
        (await _factory.ReadInvoiceTokenHashAsync(invoice.Id))
            .Should().HaveLength(64).And.NotBe(token, "only the hash is stored");

        // Sharing issues the invoice, exactly as it does for a converted one.
        (await client.GetFromJsonAsync<InvoiceDto>($"/api/invoices/{invoice.Id}"))!
            .Status.Should().Be("Sent");

        var anonymous = _factory.CreateClient();
        var publicView = (await anonymous.GetFromJsonAsync<PublicInvoiceDto>(
            $"/api/public/invoices/{token}"))!;

        publicView.InvoiceNumber.Should().Be(invoice.InvoiceNumber);
        publicView.GrandTotal.Should().Be(invoice.GrandTotal);
        publicView.Outstanding.Should().Be(invoice.GrandTotal);
    }

    [Fact]
    public async Task A_direct_invoice_can_be_paid_through_the_existing_razorpay_flow()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var customer = await CreateCustomerAsync(client);
        var invoice = await CreateDirectAsync(client, customer.Id, new object[]
        {
            new { name = "Wiring", unit = "Service", quantity = 1, unitPrice = 10000, discount = 0, taxRate = 0 }
        });

        var token = TokenOf(await CreateLinkAsync(client, invoice.Id));
        var anonymous = _factory.CreateClient();

        var orderResponse = await anonymous.PostAsync(
            $"/api/public/invoices/{token}/create-payment-order", null);
        orderResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var order = (await orderResponse.Content.ReadFromJsonAsync<PaymentOrderDto>())!;

        // The amount is the server's, derived from the invoice — 10,000 rupees in paise.
        order.Amount.Should().Be(1_000_000);

        _factory.Payments.Arrange("pay_direct_1", order.OrderId, PaymentStatus.Captured, order.Amount);

        var verified = await anonymous.PostAsJsonAsync($"/api/public/invoices/{token}/verify-payment", new
        {
            razorpayPaymentId = "pay_direct_1",
            razorpayOrderId = order.OrderId,
            razorpaySignature = FakePaymentProvider.CheckoutSignature(order.OrderId, "pay_direct_1")
        });
        verified.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = (await verified.Content.ReadFromJsonAsync<VerifyPaymentResponse>())!;
        result.Success.Should().BeTrue();
        result.InvoiceStatus.Should().Be("Paid");
        result.Outstanding.Should().Be(0m);

        var owned = (await client.GetFromJsonAsync<InvoiceDto>($"/api/invoices/{invoice.Id}"))!;
        owned.Status.Should().Be("Paid");
        owned.Paid.Should().Be(10000m);
    }

    // ---- share material --------------------------------------------------

    [Fact]
    public async Task Creating_a_link_returns_ready_made_share_material()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var customer = await CreateCustomerAsync(client);
        var invoice = await CreateDirectAsync(client, customer.Id);

        var link = await CreateLinkAsync(client, invoice.Id);
        var share = link.Share;

        share.Url.Should().Be(link.Url);
        share.Message.Should().Contain("John")
            .And.Contain(invoice.InvoiceNumber)
            .And.Contain("Test Business")
            .And.Contain(link.Url);
        share.EmailSubject.Should().Be($"Invoice {invoice.InvoiceNumber} from Test Business");
        share.CustomerPhone.Should().Be("919123456780");
        share.CustomerEmail.Should().Be("john@example.com");
        share.WhatsAppUrl.Should().StartWith("https://wa.me/919123456780?text=");
        share.MailtoUrl.Should().StartWith("mailto:john%40example.com?subject=");
    }

    [Fact]
    public async Task Share_material_asks_for_the_outstanding_balance_not_the_original_total()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var customer = await CreateCustomerAsync(client);
        var invoice = await CreateDirectAsync(client, customer.Id, new object[]
        {
            new { name = "Wiring", unit = "Service", quantity = 1, unitPrice = 10000, discount = 0, taxRate = 0 }
        });

        var token = TokenOf(await CreateLinkAsync(client, invoice.Id));
        var anonymous = _factory.CreateClient();
        var order = (await (await anonymous.PostAsync(
            $"/api/public/invoices/{token}/create-payment-order", null))
            .Content.ReadFromJsonAsync<PaymentOrderDto>())!;

        // A part payment of 3,000 of the 10,000.
        _factory.Payments.Arrange("pay_partial_share", order.OrderId, PaymentStatus.Captured, 300_000);
        (await anonymous.PostAsJsonAsync($"/api/public/invoices/{token}/verify-payment", new
        {
            razorpayPaymentId = "pay_partial_share",
            razorpayOrderId = order.OrderId,
            razorpaySignature = FakePaymentProvider.CheckoutSignature(order.OrderId, "pay_partial_share")
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var reshared = await CreateLinkAsync(client, invoice.Id);

        reshared.Share.Message.Should().Contain("₹7,000.00")
            .And.NotContain("₹10,000.00", "chasing the full total after a part payment would be wrong");
    }

    [Fact]
    public async Task Share_material_never_carries_an_internal_identifier()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var customer = await CreateCustomerAsync(client);
        var invoice = await CreateDirectAsync(client, customer.Id);

        var link = await CreateLinkAsync(client, invoice.Id);
        var everything = string.Join('\n',
            link.Share.Message, link.Share.WhatsAppUrl, link.Share.MailtoUrl, link.Share.EmailSubject);

        everything.Should().NotContain(invoice.Id.ToString())
            .And.NotContain(customer.Id.ToString())
            .And.NotContain("eyJ", "no JWT may appear in anything the customer receives");

        // The token appears only as part of the public URL, never on its own.
        var token = TokenOf(link);
        foreach (var url in new[] { link.Share.WhatsAppUrl, link.Share.MailtoUrl })
            Uri.UnescapeDataString(url).Should().NotContain(token + " ")
                .And.NotContain("\n" + token);
    }

    // ---- the quotation path is untouched ---------------------------------

    [Fact]
    public async Task Quotation_conversion_still_works_exactly_as_before()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var customer = await CreateCustomerAsync(client);

        var quotation = (await (await client.PostAsJsonAsync("/api/quotations", new
        {
            customerId = customer.Id,
            quotationDate = Today.ToString("yyyy-MM-dd"),
            validUntil = Today.AddDays(15).ToString("yyyy-MM-dd"),
            status = "Accepted",
            items = DefaultItems
        })).Content.ReadFromJsonAsync<QuotationDto>())!;

        var response = await client.PostAsync($"/api/quotations/{quotation.Id}/convert-to-invoice", null);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var invoice = (await response.Content.ReadFromJsonAsync<InvoiceDto>())!;

        invoice.QuotationId.Should().Be(quotation.Id);
        invoice.QuotationNumber.Should().Be(quotation.QuotationNumber);
        invoice.GrandTotal.Should().Be(quotation.GrandTotal);

        // Still one invoice per quotation.
        (await client.PostAsync($"/api/quotations/{quotation.Id}/convert-to-invoice", null))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _factory.CountInvoicesAsync(quotation.Id)).Should().Be(1);

        // And the list still reports where it came from.
        var list = (await client.GetFromJsonAsync<PagedResult<InvoiceListItemDto>>(
            $"/api/invoices?search={quotation.QuotationNumber}"))!;
        list.Items.Should().ContainSingle(i => i.Id == invoice.Id);
    }

    [Fact]
    public async Task Direct_and_converted_invoices_appear_in_one_list()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var customer = await CreateCustomerAsync(client);

        var direct = await CreateDirectAsync(client, customer.Id);

        var quotation = (await (await client.PostAsJsonAsync("/api/quotations", new
        {
            customerId = customer.Id,
            quotationDate = Today.ToString("yyyy-MM-dd"),
            validUntil = Today.AddDays(15).ToString("yyyy-MM-dd"),
            status = "Accepted",
            items = DefaultItems
        })).Content.ReadFromJsonAsync<QuotationDto>())!;
        var converted = (await (await client.PostAsync(
            $"/api/quotations/{quotation.Id}/convert-to-invoice", null))
            .Content.ReadFromJsonAsync<InvoiceDto>())!;

        var list = (await client.GetFromJsonAsync<PagedResult<InvoiceListItemDto>>("/api/invoices"))!;

        list.Items.Should().HaveCount(2);
        list.Items.Should().ContainSingle(i => i.Id == direct.Id)
            .Which.QuotationNumber.Should().BeEmpty();
        list.Items.Should().ContainSingle(i => i.Id == converted.Id)
            .Which.QuotationNumber.Should().Be(quotation.QuotationNumber);
    }
}
