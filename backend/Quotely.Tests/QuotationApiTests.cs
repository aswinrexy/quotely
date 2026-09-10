using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Quotely.Api.DTOs;
using Xunit;

namespace Quotely.Tests;

public class QuotationApiTests : IClassFixture<QuotelyApiFactory>
{
    private readonly QuotelyApiFactory _factory;

    public QuotationApiTests(QuotelyApiFactory factory) => _factory = factory;

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static object QuotationPayload(Guid customerId, object[]? items = null, string? validUntil = null) => new
    {
        customerId,
        quotationDate = Today.ToString("yyyy-MM-dd"),
        validUntil = validUntil ?? Today.AddDays(15).ToString("yyyy-MM-dd"),
        notes = "Thanks!",
        terms = "Valid until the stated date.",
        items = items ?? new object[]
        {
            new { name = "AC Installation", unit = "Service", quantity = 2, unitPrice = 5000, discount = 500, taxRate = 18 },
            new { name = "AC Maintenance", unit = "Service", quantity = 1, unitPrice = 1500, discount = 0, taxRate = 18 }
        }
    };

    private static async Task<CustomerDto> CreateCustomerAsync(HttpClient client, string name = "John Smith")
    {
        var response = await client.PostAsJsonAsync("/api/customers", new { name, companyName = $"{name} Ltd" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<CustomerDto>())!;
    }

    [Fact]
    public async Task Protected_endpoints_reject_anonymous_requests()
    {
        var client = _factory.CreateClient();

        foreach (var path in new[] { "/api/quotations", "/api/customers", "/api/products", "/api/business-profile" })
        {
            var response = await client.GetAsync(path);
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"{path} must require a token");
        }
    }

    [Fact]
    public async Task Backend_recalculates_totals_and_ignores_client_supplied_values()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var customer = await CreateCustomerAsync(client);

        var response = await client.PostAsJsonAsync("/api/quotations", QuotationPayload(customer.Id));
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var quotation = (await response.Content.ReadFromJsonAsync<QuotationDto>())!;
        quotation.Subtotal.Should().Be(11500m);
        quotation.DiscountTotal.Should().Be(500m);
        quotation.TaxTotal.Should().Be(1980m);
        quotation.GrandTotal.Should().Be(12980m);
        quotation.Status.Should().Be("Draft");
        quotation.QuotationNumber.Should().MatchRegex(@"^QT-\d{6}$");
    }

    [Fact]
    public async Task Quotation_numbers_are_sequential_and_restart_per_user()
    {
        var first = await _factory.CreateSignedInClientAsync();
        var firstCustomer = await CreateCustomerAsync(first);

        var a = await (await first.PostAsJsonAsync("/api/quotations", QuotationPayload(firstCustomer.Id)))
            .Content.ReadFromJsonAsync<QuotationDto>();
        var b = await (await first.PostAsJsonAsync("/api/quotations", QuotationPayload(firstCustomer.Id)))
            .Content.ReadFromJsonAsync<QuotationDto>();

        a!.QuotationNumber.Should().Be("QT-000001");
        b!.QuotationNumber.Should().Be("QT-000002");

        var second = await _factory.CreateSignedInClientAsync();
        var secondCustomer = await CreateCustomerAsync(second, "Priya Raman");
        var c = await (await second.PostAsJsonAsync("/api/quotations", QuotationPayload(secondCustomer.Id)))
            .Content.ReadFromJsonAsync<QuotationDto>();

        c!.QuotationNumber.Should().Be("QT-000001");
    }

    [Fact]
    public async Task A_user_cannot_read_update_or_delete_another_users_quotation()
    {
        var owner = await _factory.CreateSignedInClientAsync();
        var customer = await CreateCustomerAsync(owner);
        var quotation = await (await owner.PostAsJsonAsync("/api/quotations", QuotationPayload(customer.Id)))
            .Content.ReadFromJsonAsync<QuotationDto>();

        var intruder = await _factory.CreateSignedInClientAsync();

        (await intruder.GetAsync($"/api/quotations/{quotation!.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await intruder.PutAsJsonAsync($"/api/quotations/{quotation.Id}", QuotationPayload(customer.Id)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await intruder.DeleteAsync($"/api/quotations/{quotation.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await intruder.PostAsync($"/api/quotations/{quotation.Id}/pdf", null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_quotation_cannot_be_attached_to_another_users_customer()
    {
        var owner = await _factory.CreateSignedInClientAsync();
        var customer = await CreateCustomerAsync(owner);

        var intruder = await _factory.CreateSignedInClientAsync();
        var response = await intruder.PostAsJsonAsync("/api/quotations", QuotationPayload(customer.Id));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_user_only_sees_their_own_customers_and_quotations()
    {
        var owner = await _factory.CreateSignedInClientAsync();
        var customer = await CreateCustomerAsync(owner);
        await owner.PostAsJsonAsync("/api/quotations", QuotationPayload(customer.Id));

        var other = await _factory.CreateSignedInClientAsync();

        (await other.GetFromJsonAsync<PagedResult<QuotationListItemDto>>("/api/quotations"))!
            .TotalCount.Should().Be(0);
        (await other.GetFromJsonAsync<PagedResult<CustomerDto>>("/api/customers"))!
            .TotalCount.Should().Be(0);
        (await other.GetAsync($"/api/customers/{customer.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("no-items")]
    [InlineData("zero-quantity")]
    [InlineData("negative-price")]
    [InlineData("negative-tax")]
    [InlineData("bad-dates")]
    public async Task Invalid_quotation_requests_are_rejected(string scenario)
    {
        var client = await _factory.CreateSignedInClientAsync();
        var customer = await CreateCustomerAsync(client);

        var payload = scenario switch
        {
            "no-items" => QuotationPayload(customer.Id, Array.Empty<object>()),
            "zero-quantity" => QuotationPayload(customer.Id, new object[]
                { new { name = "Wiring", unit = "Hour", quantity = 0, unitPrice = 800, discount = 0, taxRate = 18 } }),
            "negative-price" => QuotationPayload(customer.Id, new object[]
                { new { name = "Wiring", unit = "Hour", quantity = 1, unitPrice = -10, discount = 0, taxRate = 18 } }),
            "negative-tax" => QuotationPayload(customer.Id, new object[]
                { new { name = "Wiring", unit = "Hour", quantity = 1, unitPrice = 800, discount = 0, taxRate = -5 } }),
            _ => QuotationPayload(customer.Id, validUntil: Today.AddDays(-5).ToString("yyyy-MM-dd"))
        };

        var response = await client.PostAsJsonAsync("/api/quotations", payload);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("at Quotely.Api", "internal details must never reach the client");
    }

    [Fact]
    public async Task Missing_quotation_returns_a_clean_404_envelope()
    {
        var client = await _factory.CreateSignedInClientAsync();

        var response = await client.GetAsync($"/api/quotations/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var error = await response.Content.ReadFromJsonAsync<ApiError>();
        error!.Message.Should().Be("Quotation was not found.");
    }

    [Fact]
    public async Task Updating_a_quotation_replaces_its_items_and_recalculates()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var customer = await CreateCustomerAsync(client);
        var created = await (await client.PostAsJsonAsync("/api/quotations", QuotationPayload(customer.Id)))
            .Content.ReadFromJsonAsync<QuotationDto>();

        var update = new
        {
            customerId = customer.Id,
            quotationDate = Today.ToString("yyyy-MM-dd"),
            validUntil = Today.AddDays(30).ToString("yyyy-MM-dd"),
            status = "Sent",
            items = new object[]
            {
                new { name = "Site Inspection", unit = "Visit", quantity = 1, unitPrice = 750, discount = 0, taxRate = 0 }
            }
        };

        var response = await client.PutAsJsonAsync($"/api/quotations/{created!.Id}", update);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = (await response.Content.ReadFromJsonAsync<QuotationDto>())!;
        updated.Items.Should().HaveCount(1);
        updated.GrandTotal.Should().Be(750m);
        updated.Status.Should().Be("Sent");
        updated.QuotationNumber.Should().Be(created.QuotationNumber, "the number is stable across edits");
    }

    [Fact]
    public async Task Dashboard_stats_count_only_the_signed_in_users_quotations()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var customer = await CreateCustomerAsync(client);
        await client.PostAsJsonAsync("/api/quotations", QuotationPayload(customer.Id));

        var stats = await client.GetFromJsonAsync<DashboardStatsDto>("/api/quotations/stats");

        stats!.TotalQuotations.Should().Be(1);
        stats.DraftCount.Should().Be(1);
        stats.SentCount.Should().Be(0);
        stats.TotalValue.Should().Be(12980m);
    }

    private record ApiError(string Message, int Status);
}
