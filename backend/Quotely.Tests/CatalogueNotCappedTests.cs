using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Quotely.Tests;

/// <summary>
/// Enforcement on, limits left at their SHIPPED defaults.
///
/// <see cref="FreeTierTests"/> shrinks every allowance so a test can reach it, which is the right
/// way to prove counting works and the wrong way to notice that a default changed. This fixture
/// deliberately overrides nothing but the two switches, so what is asserted here is the figure a
/// real deployment would actually use.
/// </summary>
public class DefaultLimitsFactory : QuotelyApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Billing:Enabled"] = "true",
            ["Billing:EnforceEntitlements"] = "true",
            // Nothing else. Billing:WithoutSubscription:* stays at whatever BillingOptions ships.
        }));
    }
}

/// <summary>
/// The catalogue is not one of the free tier's counts.
///
/// It was capped at ten, and that stopped making sense the moment every invoice and quotation line
/// had to be priced from the catalogue. Free text used to be the way round a full catalogue; with
/// it gone, a business at the cap could not bill for an eleventh distinct thing at all — unable to
/// invoice work it had already done. These tests exist so that cap cannot come back by accident.
/// </summary>
public class CatalogueNotCappedTests : IClassFixture<DefaultLimitsFactory>
{
    private readonly DefaultLimitsFactory _factory;

    public CatalogueNotCappedTests(DefaultLimitsFactory factory) => _factory = factory;

    private static Task<HttpResponseMessage> CreateProductAsync(HttpClient client, string name) =>
        client.PostAsJsonAsync("/api/products", new { name, unit = "Service", price = 500m, taxRate = 18m });

    private static Task<HttpResponseMessage> CreateCustomerAsync(HttpClient client, string name) =>
        client.PostAsJsonAsync("/api/customers", new { name, city = "Kochi" });

    [Fact]
    public async Task A_free_account_can_build_a_catalogue_well_past_the_old_limit()
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false, seedCatalogue: false);
        await _factory.ExpireTrialAsync(client);

        // Twenty-five is twice the old cap and then some. If a limit is ever reintroduced at any
        // plausible figure, this fails rather than quietly capping a real business.
        for (var i = 0; i < 25; i++)
            (await CreateProductAsync(client, $"Service {i}")).StatusCode
                .Should().Be(HttpStatusCode.Created, "product {0} must be allowed on the free tier", i);
    }

    [Fact]
    public async Task The_entitlements_summary_reports_no_limit_on_products()
    {
        // The browser reads this to decide whether to show "8 of 10 used". A stale limit here would
        // nag someone about a ceiling that no longer exists.
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false, seedCatalogue: false);
        await _factory.ExpireTrialAsync(client);

        var response = await client.GetAsync("/api/billing/entitlements");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"products\"");
        // Customers still carry one, which also proves this account really is being enforced and
        // the products result is not just enforcement being off.
        body.Should().MatchRegex("\"products\":\\{[^}]*\"limit\":null");
        body.Should().MatchRegex("\"customers\":\\{[^}]*\"limit\":5");
    }

    [Fact]
    public async Task Customers_are_still_capped_at_five()
    {
        // The counterweight. Removing the catalogue cap was a decision about the catalogue, not a
        // retreat from the free tier — if this ever passes vacuously, the tier has no teeth left.
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false, seedCatalogue: false);
        await _factory.ExpireTrialAsync(client);

        // One customer already exists from registration in some factories; count from what the API
        // reports rather than assuming, so the assertion is about the limit and not the fixture.
        for (var i = 0; i < 5; i++) await CreateCustomerAsync(client, $"Customer {i}");

        (await CreateCustomerAsync(client, "One too many")).StatusCode
            .Should().Be(HttpStatusCode.PaymentRequired);
    }
}
