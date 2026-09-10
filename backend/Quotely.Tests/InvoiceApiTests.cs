using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Quotely.Api.DTOs;
using Xunit;

namespace Quotely.Tests;

/// <summary>
/// V2.2 — quotation → invoice conversion, ownership, snapshotting and the invoice PDF.
/// </summary>
public class InvoiceApiTests : IClassFixture<QuotelyApiFactory>
{
    private readonly QuotelyApiFactory _factory;

    public InvoiceApiTests(QuotelyApiFactory factory) => _factory = factory;

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    // ---- helpers -------------------------------------------------------

    private static async Task<CustomerDto> CreateCustomerAsync(HttpClient client, string name = "John Smith")
    {
        var response = await client.PostAsJsonAsync("/api/customers", new
        {
            name,
            companyName = $"{name} Construction",
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
        new { name = "AC Installation", description = "Split AC, wall mounted", unit = "Service", quantity = 2, unitPrice = 5000, discount = 0, taxRate = 18 },
        new { name = "Annual Maintenance", unit = "Service", quantity = 1, unitPrice = 2000, discount = 0, taxRate = 18 }
    };

    private static async Task<QuotationDto> CreateQuotationAsync(
        HttpClient client, Guid customerId, object[]? items = null,
        DateOnly? validUntil = null, DateOnly? quotationDate = null)
    {
        var response = await client.PostAsJsonAsync("/api/quotations", new
        {
            customerId,
            quotationDate = (quotationDate ?? Today).ToString("yyyy-MM-dd"),
            validUntil = (validUntil ?? Today.AddDays(15)).ToString("yyyy-MM-dd"),
            notes = "Thanks for your business.",
            terms = "Payment due within 15 days.",
            items = items ?? DefaultItems
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<QuotationDto>())!;
    }

    /// <summary>Re-saves the quotation with a new status, which is how the owner moves it along.</summary>
    private static async Task<QuotationDto> SetStatusAsync(HttpClient client, QuotationDto quotation, string status)
    {
        var response = await client.PutAsJsonAsync($"/api/quotations/{quotation.Id}", new
        {
            customerId = quotation.Customer.Id,
            quotationDate = quotation.QuotationDate.ToString("yyyy-MM-dd"),
            validUntil = quotation.ValidUntil.ToString("yyyy-MM-dd"),
            notes = quotation.Notes,
            terms = quotation.Terms,
            status,
            items = quotation.Items.Select(i => new
            {
                name = i.Name,
                description = i.Description,
                unit = i.Unit,
                quantity = i.Quantity,
                unitPrice = i.UnitPrice,
                discount = i.Discount,
                taxRate = i.TaxRate
            }).ToArray()
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<QuotationDto>())!;
    }

    private static async Task<QuotationDto> AcceptedQuotationAsync(HttpClient client, string customerName = "John Smith")
    {
        var customer = await CreateCustomerAsync(client, customerName);
        var quotation = await CreateQuotationAsync(client, customer.Id);
        return await SetStatusAsync(client, quotation, "Accepted");
    }

    private static async Task<InvoiceDto> ConvertAsync(HttpClient client, Guid quotationId)
    {
        var response = await client.PostAsync($"/api/quotations/{quotationId}/convert-to-invoice", null);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<InvoiceDto>())!;
    }

    private static object UpdatePayload(InvoiceDto invoice, string? status = null, object[]? items = null) => new
    {
        invoiceDate = invoice.InvoiceDate.ToString("yyyy-MM-dd"),
        dueDate = invoice.DueDate.ToString("yyyy-MM-dd"),
        status = status ?? invoice.Status,
        notes = invoice.Notes,
        terms = invoice.Terms,
        items
    };

    // ---- conversion ----------------------------------------------------

    [Fact]
    public async Task An_accepted_quotation_converts_into_an_invoice()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var quotation = await AcceptedQuotationAsync(client);

        var invoice = await ConvertAsync(client, quotation.Id);

        invoice.InvoiceNumber.Should().MatchRegex(@"^INV-\d{6}$");
        invoice.Status.Should().Be("Draft");
        invoice.QuotationId.Should().Be(quotation.Id);
        invoice.QuotationNumber.Should().Be(quotation.QuotationNumber);
        invoice.Items.Should().HaveCount(quotation.Items.Count);
        invoice.GrandTotal.Should().Be(quotation.GrandTotal);
    }

    [Theory]
    [InlineData("Draft")]
    [InlineData("Sent")]
    [InlineData("Rejected")]
    [InlineData("Expired")]
    public async Task Only_accepted_quotations_can_be_converted(string status)
    {
        var client = await _factory.CreateSignedInClientAsync();
        var customer = await CreateCustomerAsync(client);
        var quotation = await CreateQuotationAsync(client, customer.Id);
        if (status != "Draft") await SetStatusAsync(client, quotation, status);

        var response = await client.PostAsync($"/api/quotations/{quotation.Id}/convert-to-invoice", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _factory.CountInvoicesAsync(quotation.Id)).Should().Be(0);
    }

    [Fact]
    public async Task A_quotation_past_its_valid_until_date_cannot_be_converted_while_it_is_still_open()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var customer = await CreateCustomerAsync(client);
        // Past its validity and never accepted: an expired offer is not an agreement.
        var quotation = await CreateQuotationAsync(
            client, customer.Id, validUntil: Today.AddDays(-1), quotationDate: Today.AddDays(-20));
        await SetStatusAsync(client, quotation, "Sent");

        var response = await client.PostAsync($"/api/quotations/{quotation.Id}/convert-to-invoice", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _factory.CountInvoicesAsync(quotation.Id)).Should().Be(0);
    }

    [Fact]
    public async Task Converting_twice_reports_a_conflict_and_never_creates_a_second_invoice()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var quotation = await AcceptedQuotationAsync(client);
        var first = await ConvertAsync(client, quotation.Id);

        var second = await client.PostAsync($"/api/quotations/{quotation.Id}/convert-to-invoice", null);

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await second.Content.ReadAsStringAsync()).Should().Contain(first.InvoiceNumber);
        (await _factory.CountInvoicesAsync(quotation.Id)).Should().Be(1);
    }

    [Fact]
    public async Task Invoice_numbers_are_sequential_and_restart_per_user()
    {
        var first = await _factory.CreateSignedInClientAsync();
        var a = await ConvertAsync(first, (await AcceptedQuotationAsync(first)).Id);
        var b = await ConvertAsync(first, (await AcceptedQuotationAsync(first)).Id);

        a.InvoiceNumber.Should().Be("INV-000001");
        b.InvoiceNumber.Should().Be("INV-000002");

        var second = await _factory.CreateSignedInClientAsync();
        var c = await ConvertAsync(second, (await AcceptedQuotationAsync(second, "Priya Raman")).Id);

        c.InvoiceNumber.Should().Be("INV-000001");
    }

    [Fact]
    public async Task Conversion_defaults_the_due_date_to_fifteen_days_after_the_invoice_date()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await ConvertAsync(client, (await AcceptedQuotationAsync(client)).Id);

        invoice.InvoiceDate.Should().Be(Today);
        invoice.DueDate.Should().Be(Today.AddDays(15));
    }

    [Fact]
    public async Task The_quotation_reports_its_invoice_and_stops_offering_conversion()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var quotation = await AcceptedQuotationAsync(client);

        var before = await client.GetFromJsonAsync<QuotationDto>($"/api/quotations/{quotation.Id}");
        before!.CanConvertToInvoice.Should().BeTrue();
        before.InvoiceId.Should().BeNull();

        var invoice = await ConvertAsync(client, quotation.Id);

        var after = await client.GetFromJsonAsync<QuotationDto>($"/api/quotations/{quotation.Id}");
        after!.CanConvertToInvoice.Should().BeFalse();
        after.InvoiceId.Should().Be(invoice.Id);
        after.InvoiceNumber.Should().Be(invoice.InvoiceNumber);
    }

    [Fact]
    public async Task An_invoiced_quotation_cannot_be_deleted()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var quotation = await AcceptedQuotationAsync(client);
        var invoice = await ConvertAsync(client, quotation.Id);

        var blocked = await client.DeleteAsync($"/api/quotations/{quotation.Id}");
        blocked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await blocked.Content.ReadAsStringAsync()).Should().Contain(invoice.InvoiceNumber);

        // Removing the invoice releases the quotation again.
        (await client.DeleteAsync($"/api/invoices/{invoice.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.DeleteAsync($"/api/quotations/{quotation.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ---- ownership -----------------------------------------------------

    [Fact]
    public async Task Invoice_endpoints_reject_anonymous_requests()
    {
        var owner = await _factory.CreateSignedInClientAsync();
        var quotation = await AcceptedQuotationAsync(owner);
        var invoice = await ConvertAsync(owner, quotation.Id);

        var anonymous = _factory.CreateClient();

        (await anonymous.GetAsync("/api/invoices")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync($"/api/invoices/{invoice.Id}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync($"/api/invoices/{invoice.Id}/pdf")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.DeleteAsync($"/api/invoices/{invoice.Id}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsync($"/api/quotations/{quotation.Id}/convert-to-invoice", null))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task One_user_cannot_convert_another_users_quotation()
    {
        var owner = await _factory.CreateSignedInClientAsync();
        var quotation = await AcceptedQuotationAsync(owner);

        var intruder = await _factory.CreateSignedInClientAsync();
        var response = await intruder.PostAsync($"/api/quotations/{quotation.Id}/convert-to-invoice", null);

        // 404, not 403: an id belonging to someone else must not be confirmed to exist.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _factory.CountInvoicesAsync(quotation.Id)).Should().Be(0);
    }

    [Fact]
    public async Task One_user_cannot_read_update_delete_or_download_another_users_invoice()
    {
        var owner = await _factory.CreateSignedInClientAsync();
        var invoice = await ConvertAsync(owner, (await AcceptedQuotationAsync(owner)).Id);

        var intruder = await _factory.CreateSignedInClientAsync();

        (await intruder.GetAsync($"/api/invoices/{invoice.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await intruder.PutAsJsonAsync($"/api/invoices/{invoice.Id}", UpdatePayload(invoice)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await intruder.DeleteAsync($"/api/invoices/{invoice.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await intruder.GetAsync($"/api/invoices/{invoice.Id}/pdf")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // The owner's invoice is untouched by any of that.
        (await owner.GetAsync($"/api/invoices/{invoice.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_invoice_list_only_ever_contains_the_callers_own_rows()
    {
        var owner = await _factory.CreateSignedInClientAsync();
        var mine = await ConvertAsync(owner, (await AcceptedQuotationAsync(owner)).Id);

        var other = await _factory.CreateSignedInClientAsync();
        await ConvertAsync(other, (await AcceptedQuotationAsync(other, "Someone Else")).Id);

        var page = await owner.GetFromJsonAsync<PagedResult<InvoiceListItemDto>>("/api/invoices");

        page!.Items.Should().ContainSingle().Which.Id.Should().Be(mine.Id);
    }

    // ---- snapshotting --------------------------------------------------

    [Fact]
    public async Task Invoice_lines_preserve_the_quoted_description_and_price()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var quotation = await AcceptedQuotationAsync(client);
        var invoice = await ConvertAsync(client, quotation.Id);

        var line = invoice.Items[0];
        line.Name.Should().Be("AC Installation");
        line.Description.Should().Be("Split AC, wall mounted");
        line.Unit.Should().Be("Service");
        line.Quantity.Should().Be(2m);
        line.UnitPrice.Should().Be(5000m);
        line.TaxRate.Should().Be(18m);
        line.LineTotal.Should().Be(11800m);
    }

    [Fact]
    public async Task Changing_the_product_catalogue_price_does_not_restate_an_existing_invoice()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var customer = await CreateCustomerAsync(client);

        var product = await (await client.PostAsJsonAsync("/api/products", new
        {
            name = "AC Installation", unit = "Service", price = 5000, taxRate = 18
        })).Content.ReadFromJsonAsync<ProductDto>();

        var quotation = await CreateQuotationAsync(client, customer.Id, new object[]
        {
            new { productId = product!.Id, name = "AC Installation", unit = "Service", quantity = 2, unitPrice = 5000, discount = 0, taxRate = 18 }
        });
        await SetStatusAsync(client, quotation, "Accepted");
        var invoice = await ConvertAsync(client, quotation.Id);

        // The catalogue price goes up after the invoice was raised.
        (await client.PutAsJsonAsync($"/api/products/{product.Id}", new
        {
            name = "AC Installation", unit = "Service", price = 6000, taxRate = 18
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var reloaded = await client.GetFromJsonAsync<InvoiceDto>($"/api/invoices/{invoice.Id}");

        reloaded!.Items[0].UnitPrice.Should().Be(5000m);
        reloaded.GrandTotal.Should().Be(invoice.GrandTotal);
    }

    [Fact]
    public async Task Renaming_the_customer_does_not_restate_an_existing_invoice()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var customer = await CreateCustomerAsync(client);
        var quotation = await CreateQuotationAsync(client, customer.Id);
        await SetStatusAsync(client, quotation, "Accepted");
        var invoice = await ConvertAsync(client, quotation.Id);

        invoice.Customer.Name.Should().Be("John Smith");
        invoice.Customer.City.Should().Be("Chennai");

        (await client.PutAsJsonAsync($"/api/customers/{customer.Id}", new
        {
            name = "John Smith Junior", city = "Bengaluru"
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var reloaded = await client.GetFromJsonAsync<InvoiceDto>($"/api/invoices/{invoice.Id}");

        reloaded!.Customer.Name.Should().Be("John Smith");
        reloaded.Customer.City.Should().Be("Chennai");

        // ...and the customer record itself was not written to during conversion.
        var stored = await client.GetFromJsonAsync<CustomerDto>($"/api/customers/{customer.Id}");
        stored!.Name.Should().Be("John Smith Junior");
    }

    // ---- totals --------------------------------------------------------

    [Fact]
    public async Task Invoice_totals_are_computed_by_the_server_and_match_the_accepted_quotation()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var customer = await CreateCustomerAsync(client);
        var quotation = await CreateQuotationAsync(client, customer.Id, new object[]
        {
            new { name = "AC Installation", unit = "Service", quantity = 2, unitPrice = 5000, discount = 500, taxRate = 18 },
            new { name = "Maintenance", unit = "Service", quantity = 1, unitPrice = 2000, discount = 0, taxRate = 18 }
        });
        await SetStatusAsync(client, quotation, "Accepted");

        var invoice = await ConvertAsync(client, quotation.Id);

        // gross 12,000 − 500 discount = 11,500 net; tax 18% = 2,070; total 13,570.
        invoice.Subtotal.Should().Be(12000m);
        invoice.DiscountTotal.Should().Be(500m);
        invoice.TaxTotal.Should().Be(2070m);
        invoice.GrandTotal.Should().Be(13570m);
        invoice.GrandTotal.Should().Be(quotation.GrandTotal);
        invoice.Items.Sum(i => i.LineTotal).Should().Be(invoice.GrandTotal);
    }

    [Fact]
    public async Task An_edit_recalculates_the_totals_instead_of_trusting_the_client()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await ConvertAsync(client, (await AcceptedQuotationAsync(client)).Id);

        // The payload carries totals, a status and an invoice number the client has no business setting.
        var response = await client.PutAsJsonAsync($"/api/invoices/{invoice.Id}", new
        {
            invoiceDate = invoice.InvoiceDate.ToString("yyyy-MM-dd"),
            dueDate = invoice.DueDate.ToString("yyyy-MM-dd"),
            invoiceNumber = "INV-999999",
            subtotal = 1m,
            grandTotal = 1m,
            items = new object[]
            {
                new { name = "AC Installation", unit = "Service", quantity = 1, unitPrice = 1000, discount = 0, taxRate = 10 }
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await response.Content.ReadFromJsonAsync<InvoiceDto>())!;

        updated.InvoiceNumber.Should().Be(invoice.InvoiceNumber);
        updated.Subtotal.Should().Be(1000m);
        updated.TaxTotal.Should().Be(100m);
        updated.GrandTotal.Should().Be(1100m);
    }

    [Fact]
    public async Task A_due_date_before_the_invoice_date_is_rejected()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await ConvertAsync(client, (await AcceptedQuotationAsync(client)).Id);

        var response = await client.PutAsJsonAsync($"/api/invoices/{invoice.Id}", new
        {
            invoiceDate = Today.ToString("yyyy-MM-dd"),
            dueDate = Today.AddDays(-1).ToString("yyyy-MM-dd")
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Negative_and_empty_lines_are_rejected()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await ConvertAsync(client, (await AcceptedQuotationAsync(client)).Id);

        var negative = await client.PutAsJsonAsync($"/api/invoices/{invoice.Id}", UpdatePayload(invoice, items: new object[]
        {
            new { name = "Refund", unit = "Service", quantity = 1, unitPrice = -500, discount = 0, taxRate = 18 }
        }));
        negative.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var empty = await client.PutAsJsonAsync($"/api/invoices/{invoice.Id}",
            UpdatePayload(invoice, items: Array.Empty<object>()));
        empty.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---- status rules --------------------------------------------------

    [Fact]
    public async Task Line_items_can_only_be_rewritten_while_the_invoice_is_a_draft()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await ConvertAsync(client, (await AcceptedQuotationAsync(client)).Id);
        invoice.CanEditItems.Should().BeTrue();

        var sent = (await (await client.PutAsJsonAsync($"/api/invoices/{invoice.Id}", UpdatePayload(invoice, "Sent")))
            .Content.ReadFromJsonAsync<InvoiceDto>())!;
        sent.Status.Should().Be("Sent");
        sent.CanEditItems.Should().BeFalse();
        sent.CanEdit.Should().BeTrue();

        var response = await client.PutAsJsonAsync($"/api/invoices/{invoice.Id}", UpdatePayload(sent, items: new object[]
        {
            new { name = "Sneaky change", unit = "Service", quantity = 1, unitPrice = 1, discount = 0, taxRate = 0 }
        }));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var unchanged = await client.GetFromJsonAsync<InvoiceDto>($"/api/invoices/{invoice.Id}");
        unchanged!.GrandTotal.Should().Be(invoice.GrandTotal);
    }

    [Fact]
    public async Task A_paid_invoice_can_neither_be_edited_nor_deleted()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await ConvertAsync(client, (await AcceptedQuotationAsync(client)).Id);

        var paid = (await (await client.PutAsJsonAsync($"/api/invoices/{invoice.Id}", UpdatePayload(invoice, "Paid")))
            .Content.ReadFromJsonAsync<InvoiceDto>())!;
        paid.Status.Should().Be("Paid");
        paid.CanEdit.Should().BeFalse();
        paid.CanDelete.Should().BeFalse();

        (await client.PutAsJsonAsync($"/api/invoices/{invoice.Id}", UpdatePayload(paid, "Draft")))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.DeleteAsync($"/api/invoices/{invoice.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        var stored = await client.GetFromJsonAsync<InvoiceDto>($"/api/invoices/{invoice.Id}");
        stored!.Status.Should().Be("Paid");
    }

    [Fact]
    public async Task An_invoice_past_its_due_date_is_flagged_as_overdue()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await ConvertAsync(client, (await AcceptedQuotationAsync(client)).Id);
        invoice.IsOverdue.Should().BeFalse();

        var response = await client.PutAsJsonAsync($"/api/invoices/{invoice.Id}", new
        {
            invoiceDate = Today.AddDays(-40).ToString("yyyy-MM-dd"),
            dueDate = Today.AddDays(-25).ToString("yyyy-MM-dd"),
            status = "Sent"
        });

        var overdue = (await response.Content.ReadFromJsonAsync<InvoiceDto>())!;
        overdue.IsOverdue.Should().BeTrue();

        // Settling it clears the flag without any second expiry mechanism.
        var paid = (await (await client.PutAsJsonAsync($"/api/invoices/{invoice.Id}", UpdatePayload(overdue, "Paid")))
            .Content.ReadFromJsonAsync<InvoiceDto>())!;
        paid.IsOverdue.Should().BeFalse();
    }

    [Fact]
    public async Task An_unknown_invoice_status_is_rejected()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await ConvertAsync(client, (await AcceptedQuotationAsync(client)).Id);

        (await client.PutAsJsonAsync($"/api/invoices/{invoice.Id}", UpdatePayload(invoice, "Refunded")))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---- list ----------------------------------------------------------

    [Fact]
    public async Task The_list_supports_search_and_status_filtering()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var first = await ConvertAsync(client, (await AcceptedQuotationAsync(client, "Anita Rao")).Id);
        var second = await ConvertAsync(client, (await AcceptedQuotationAsync(client, "Vikram Nair")).Id);
        await client.PutAsJsonAsync($"/api/invoices/{second.Id}", UpdatePayload(second, "Sent"));

        var searched = await client.GetFromJsonAsync<PagedResult<InvoiceListItemDto>>("/api/invoices?search=Anita");
        searched!.Items.Should().ContainSingle().Which.InvoiceNumber.Should().Be(first.InvoiceNumber);

        var byNumber = await client.GetFromJsonAsync<PagedResult<InvoiceListItemDto>>(
            $"/api/invoices?search={second.InvoiceNumber}");
        byNumber!.Items.Should().ContainSingle().Which.Id.Should().Be(second.Id);

        var drafts = await client.GetFromJsonAsync<PagedResult<InvoiceListItemDto>>("/api/invoices?status=Draft");
        drafts!.Items.Should().ContainSingle().Which.Id.Should().Be(first.Id);

        var sent = await client.GetFromJsonAsync<PagedResult<InvoiceListItemDto>>("/api/invoices?status=Sent");
        sent!.Items.Should().ContainSingle().Which.Id.Should().Be(second.Id);
    }

    // ---- pdf -----------------------------------------------------------

    [Fact]
    public async Task The_invoice_pdf_endpoint_returns_a_downloadable_document()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await ConvertAsync(client, (await AcceptedQuotationAsync(client, "Priya Raman")).Id);

        var response = await client.GetAsync($"/api/invoices/{invoice.Id}/pdf");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        response.Content.Headers.ContentDisposition!.FileName
            .Should().Contain(invoice.InvoiceNumber).And.Contain("Priya-Raman");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
        bytes.Length.Should().BeGreaterThan(1000);
    }
}
