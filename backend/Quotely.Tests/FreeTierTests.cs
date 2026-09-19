using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Quotely.Api.Billing;
using Quotely.Api.Data;
using Quotely.Api.DTOs;
using Quotely.Api.Models;
using Xunit;

namespace Quotely.Tests;

/// <summary>
/// The free tier: what a business can do without paying, and where it stops.
///
/// Enforcement is off in the ordinary suite, so this factory turns it on and shrinks the limits
/// to something a test can reach. The limits themselves are configuration; what is asserted here
/// is that they are honoured, that they stop the NEXT one rather than the ones already made, and
/// that a trial is never subject to them.
/// </summary>
public class FreeTierFactory : QuotelyApiFactory
{
    /// <summary>Small enough to exhaust in a test, large enough to prove counting works.</summary>
    public const int MaxInvoices = 3;
    public const int MaxCustomers = 2;
    public const int MaxProducts = 2;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Billing:Enabled"] = "true",
            ["Billing:EnforceEntitlements"] = "true",
            ["Billing:WithoutSubscription:MaxInvoices"] = MaxInvoices.ToString(),
            ["Billing:WithoutSubscription:MaxCustomers"] = MaxCustomers.ToString(),
            ["Billing:WithoutSubscription:MaxProducts"] = MaxProducts.ToString(),
        }));
    }

    /// <summary>
    /// Ends the trial, so the account falls to the free tier. Done by moving the dates rather
    /// than by waiting, and through the same fields a real expiry would set.
    /// </summary>
    public async Task ExpireTrialAsync(HttpClient client)
    {
        var email = await EmailOfAsync(client);

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await db.Users.FirstAsync(u => u.Email == email);
        var subscription = await db.Subscriptions.FirstAsync(s => s.UserId == user.Id);

        subscription.TrialEnd = DateTime.UtcNow.AddDays(-1);
        subscription.Status = SubscriptionStatus.Expired;
        await db.SaveChangesAsync();
    }

    private static async Task<string> EmailOfAsync(HttpClient client)
    {
        var me = await client.GetFromJsonAsync<SubscriptionDto>("/api/billing/subscription", Json);
        me.Should().NotBeNull();

        // The subscription response carries no email, so the account is identified by the token
        // the client is already holding — decoded rather than guessed.
        var token = client.DefaultRequestHeaders.Authorization!.Parameter!;
        var payload = token.Split('.')[1];
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=')
            .Replace('-', '+').Replace('_', '/');
        using var json = System.Text.Json.JsonDocument.Parse(Convert.FromBase64String(payload));
        return json.RootElement.GetProperty("email").GetString()!;
    }
}

public class FreeTierTests : IClassFixture<FreeTierFactory>
{
    private readonly FreeTierFactory _factory;

    public FreeTierTests(FreeTierFactory factory) => _factory = factory;

    // ---- helpers ---------------------------------------------------------

    private static Task<HttpResponseMessage> CreateCustomerAsync(HttpClient client, string name) =>
        client.PostAsJsonAsync("/api/customers", new { name, city = "Kochi" });

    private static Task<HttpResponseMessage> CreateProductAsync(HttpClient client, string name) =>
        client.PostAsJsonAsync("/api/products", new { name, unit = "Service", price = 500m, taxRate = 18m });

    private static Task<HttpResponseMessage> CreateInvoiceAsync(HttpClient client, Guid customerId) =>
        client.PostAsJsonAsync("/api/invoices", new
        {
            customerId,
            invoiceDate = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
            dueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)).ToString("yyyy-MM-dd"),
            items = new object[]
            {
                new { name = "Work", unit = "Service", quantity = 1, unitPrice = 500, discount = 0, taxRate = 0 }
            }
        });

    private static Task<HttpResponseMessage> CreateQuotationAsync(HttpClient client, Guid customerId) =>
        client.PostAsJsonAsync("/api/quotations", new
        {
            customerId,
            quotationDate = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
            validUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(15)).ToString("yyyy-MM-dd"),
            items = new object[]
            {
                new { name = "Work", unit = "Service", quantity = 1, unitPrice = 500, discount = 0, taxRate = 0 }
            }
        });

    private static async Task<Guid> FirstCustomerIdAsync(HttpClient client)
    {
        var response = await CreateCustomerAsync(client, $"Customer {Guid.NewGuid():N}");
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<CustomerDto>(QuotelyApiFactory.Json))!.Id;
    }

    // =====================================================================
    // 1. A trial is never limited
    // =====================================================================

    [Fact]
    public async Task A_trial_account_has_no_limits_at_all()
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);

        var entitlements = await client.GetFromJsonAsync<EntitlementSummary>(
            "/api/billing/entitlements", QuotelyApiFactory.Json);

        entitlements!.HasAccess.Should().BeTrue();
        entitlements.CanCreateQuotation.Should().BeTrue();
        entitlements.CanConnectPayments.Should().BeTrue();
        entitlements.Invoices.HasLimit.Should().BeFalse();
        entitlements.Customers.HasLimit.Should().BeFalse();
        entitlements.Products.HasLimit.Should().BeFalse();

        // And the limits genuinely do not bite: more customers than the free allowance.
        for (var i = 0; i < FreeTierFactory.MaxCustomers + 2; i++)
            (await CreateCustomerAsync(client, $"Trial customer {i}")).StatusCode
                .Should().Be(HttpStatusCode.Created);
    }

    // =====================================================================
    // 2. Once the trial ends, the counted allowances apply
    // =====================================================================

    [Fact]
    public async Task Customers_stop_at_the_free_allowance()
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);
        await _factory.ExpireTrialAsync(client);

        for (var i = 0; i < FreeTierFactory.MaxCustomers; i++)
            (await CreateCustomerAsync(client, $"Customer {i}")).StatusCode
                .Should().Be(HttpStatusCode.Created);

        var refused = await CreateCustomerAsync(client, "One too many");

        refused.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);

        var body = await refused.Content.ReadAsStringAsync();
        body.Should().Contain("free plan").And.Contain("Quotely Pro");
        // The figures travel with the refusal, so the page can say "2 of 2".
        body.Should().Contain("\"limit\":2");
    }

    [Fact]
    public async Task Products_stop_at_the_free_allowance()
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);
        await _factory.ExpireTrialAsync(client);

        for (var i = 0; i < FreeTierFactory.MaxProducts; i++)
            (await CreateProductAsync(client, $"Product {i}")).StatusCode.Should().Be(HttpStatusCode.Created);

        (await CreateProductAsync(client, "One too many")).StatusCode
            .Should().Be(HttpStatusCode.PaymentRequired);
    }

    [Fact]
    public async Task Invoices_stop_at_the_free_allowance()
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);
        var customerId = await FirstCustomerIdAsync(client);
        await _factory.ExpireTrialAsync(client);

        for (var i = 0; i < FreeTierFactory.MaxInvoices; i++)
            (await CreateInvoiceAsync(client, customerId)).StatusCode.Should().Be(HttpStatusCode.Created);

        (await CreateInvoiceAsync(client, customerId)).StatusCode
            .Should().Be(HttpStatusCode.PaymentRequired);
    }

    // =====================================================================
    // 3. Quotations and payment connections are paid features outright
    // =====================================================================

    [Fact]
    public async Task Quotations_are_refused_entirely_on_the_free_tier()
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);
        var customerId = await FirstCustomerIdAsync(client);
        await _factory.ExpireTrialAsync(client);

        var refused = await CreateQuotationAsync(client, customerId);

        refused.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("Quotations are part of Quotely Pro");
    }

    [Fact]
    public async Task Connecting_a_payment_account_is_refused_on_the_free_tier()
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);
        await _factory.ExpireTrialAsync(client);

        var refused = await client.PostAsJsonAsync("/api/settings/payments/razorpay/keys", new
        {
            keyId = FakePaymentProvider.TestKeyId,
            keySecret = FakePaymentProvider.TestKeySecret,
        });

        refused.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);

        // …and the reason names the alternative, rather than just saying no.
        (await refused.Content.ReadAsStringAsync()).Should().Contain("cash, cheque and bank transfers");
    }

    // =====================================================================
    // 4. What a limit does NOT take away
    // =====================================================================

    [Fact]
    public async Task Reaching_a_limit_never_removes_what_was_already_created()
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);

        // Created while on trial, comfortably past the free allowance.
        var ids = new List<Guid>();
        for (var i = 0; i < FreeTierFactory.MaxCustomers + 3; i++)
            ids.Add((await CreateCustomerAsync(client, $"Customer {i}"))
                .Content.ReadFromJsonAsync<CustomerDto>(QuotelyApiFactory.Json).Result!.Id);

        await _factory.ExpireTrialAsync(client);

        // Every one of them is still there, still readable.
        foreach (var id in ids)
            (await client.GetAsync($"/api/customers/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Only the next one is refused.
        (await CreateCustomerAsync(client, "One more")).StatusCode
            .Should().Be(HttpStatusCode.PaymentRequired);
    }

    [Fact]
    public async Task An_expired_account_can_still_read_edit_and_export()
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);
        var customerId = await FirstCustomerIdAsync(client);
        var invoice = (await (await CreateInvoiceAsync(client, customerId))
            .Content.ReadFromJsonAsync<InvoiceDto>(QuotelyApiFactory.Json))!;

        await _factory.ExpireTrialAsync(client);

        // Reading their own records.
        (await client.GetAsync("/api/invoices")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/api/customers")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync($"/api/invoices/{invoice.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Getting their data out. Withholding this would be holding it hostage.
        (await client.PostAsync($"/api/invoices/{invoice.Id}/pdf", null)).StatusCode
            .Should().Be(HttpStatusCode.OK);

        // And reaching the page that would fix the situation.
        (await client.GetAsync("/api/billing/subscription")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // =====================================================================
    // 5. What the interface is told
    // =====================================================================

    [Fact]
    public async Task The_entitlements_endpoint_reports_usage_against_each_allowance()
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);
        await CreateCustomerAsync(client, "Only customer");
        await _factory.ExpireTrialAsync(client);

        var entitlements = await client.GetFromJsonAsync<EntitlementSummary>(
            "/api/billing/entitlements", QuotelyApiFactory.Json);

        entitlements!.HasAccess.Should().BeFalse();
        entitlements.CanCreateQuotation.Should().BeFalse();
        entitlements.CanConnectPayments.Should().BeFalse();

        entitlements.Customers.Used.Should().Be(1);
        entitlements.Customers.Limit.Should().Be(FreeTierFactory.MaxCustomers);
        entitlements.Customers.Remaining.Should().Be(FreeTierFactory.MaxCustomers - 1);
        entitlements.Customers.Exhausted.Should().BeFalse();

        entitlements.Invoices.Limit.Should().Be(FreeTierFactory.MaxInvoices);

        // Recording money received by hand is never withheld — a free business still gets paid,
        // it just cannot put a Pay button on the invoice.
        entitlements.CanAcceptPayments.Should().BeTrue();
        entitlements.CanExportData.Should().BeTrue();
    }

    [Fact]
    public async Task Subscribing_again_lifts_every_limit_at_once()
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);
        await _factory.ExpireTrialAsync(client);

        (await CreateQuotationAsync(client, Guid.NewGuid())).StatusCode
            .Should().Be(HttpStatusCode.PaymentRequired);

        // QUOTELY6 puts them back in trial, which is full access.
        (await client.PostAsJsonAsync("/api/billing/coupon", new { code = WellKnownCoupons.SixMonthsFree }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var entitlements = await client.GetFromJsonAsync<EntitlementSummary>(
            "/api/billing/entitlements", QuotelyApiFactory.Json);

        entitlements!.HasAccess.Should().BeTrue();
        entitlements.CanCreateQuotation.Should().BeTrue();
        entitlements.CanConnectPayments.Should().BeTrue();
        entitlements.Customers.HasLimit.Should().BeFalse();

        var customerId = await FirstCustomerIdAsync(client);
        (await CreateQuotationAsync(client, customerId)).StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
