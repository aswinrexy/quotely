using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Quotely.Api.Data;
using Quotely.Api.DTOs;
using Quotely.Api.Models;
using Quotely.Api.Payments;
using Xunit;

namespace Quotely.Tests;

/// <summary>
/// The V2.7 production-readiness requirement stated most precisely:
///
///   <b>A caller must never be able to choose another tenant's merchant payment connection.</b>
///
/// The isolation tests in <see cref="MerchantIsolationTests"/> prove that a caller who TRIES the
/// available routes cannot cross tenants. These prove something stronger and more durable: that
/// there is no route to try. The merchant context is derived from tenant identity, and the API
/// surface offers no way to name a connection at all — so a future endpoint that accepted one
/// would fail these tests rather than silently opening the hole.
/// </summary>
public class MerchantContextDerivationTests : IClassFixture<QuotelyApiFactory>
{
    private readonly QuotelyApiFactory _factory;

    public MerchantContextDerivationTests(QuotelyApiFactory factory) => _factory = factory;

    private static Assembly Api => typeof(MerchantPaymentConnection).Assembly;

    /// <summary>Names that would let a request nominate somebody's payment account.</summary>
    private static readonly string[] ConnectionSelectors =
    {
        "connectionid",
        "merchantconnectionid",
        "merchantid",
        "provideraccountid",
        "accountid"
    };

    // =====================================================================
    // 1. There is no way to ASK for another tenant's connection
    // =====================================================================

    [Fact]
    public void No_request_body_anywhere_in_the_api_can_name_a_payment_connection()
    {
        // Every type a controller action binds from the body. If none of them carries a
        // connection identifier, no request can express "use that merchant's account".
        var offenders = Api.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.Namespace == "Quotely.Api.DTOs")
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanWrite)
                .Where(p => ConnectionSelectors.Contains(p.Name.ToLowerInvariant()))
                .Select(p => $"{t.Name}.{p.Name}"))
            .ToList();

        offenders.Should().BeEmpty(
            "a request that can name a payment connection is a request that can name someone else's");
    }

    [Fact]
    public void No_route_or_query_parameter_anywhere_in_the_api_can_name_a_payment_connection()
    {
        var offenders = new List<string>();

        foreach (var controller in Api.GetTypes().Where(t => typeof(ControllerBase).IsAssignableFrom(t)))
        {
            foreach (var action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                foreach (var parameter in action.GetParameters())
                {
                    // Only scalar parameters bind from the route or query string; a complex type
                    // binds from the body and is covered by the test above.
                    if (parameter.ParameterType != typeof(Guid) &&
                        parameter.ParameterType != typeof(Guid?) &&
                        parameter.ParameterType != typeof(string)) continue;

                    if (ConnectionSelectors.Contains(parameter.Name?.ToLowerInvariant() ?? string.Empty))
                        offenders.Add($"{controller.Name}.{action.Name}({parameter.Name})");
                }
            }
        }

        offenders.Should().BeEmpty();
    }

    [Fact]
    public void The_only_way_to_build_a_merchant_context_takes_a_tenant_and_nothing_else()
    {
        // MerchantPaymentContext is what every provider call requires. If the only methods that
        // produce one take a user id, then "whose account collects this?" is answerable only from
        // tenant identity — which is the whole architecture in one assertion.
        var producers = typeof(Quotely.Api.Services.IMerchantConnectionService)
            .GetMethods()
            .Where(m => m.ReturnType.IsGenericType &&
                        m.ReturnType.GetGenericArguments()[0] is { } arg &&
                        (arg == typeof(MerchantPaymentContext) ||
                         arg == typeof(MerchantPaymentContext) ||
                         Nullable.GetUnderlyingType(arg) == typeof(MerchantPaymentContext) ||
                         arg.Name.Contains(nameof(MerchantPaymentContext))))
            .ToList();

        producers.Should().NotBeEmpty("the resolver is the thing under test");

        foreach (var producer in producers)
        {
            var first = producer.GetParameters().First();
            first.ParameterType.Should().Be<Guid>();
            first.Name.Should().Be("userId",
                $"{producer.Name} must derive the merchant from the tenant, not from a caller's claim");
        }
    }

    [Fact]
    public void Every_provider_operation_requires_a_merchant_context()
    {
        // No overload that omits it. A method that could collect money without naming an account
        // is a method that will eventually collect into the wrong one.
        var moneyMoving = new[]
        {
            nameof(IMerchantPaymentProvider.CreateOrderAsync),
            nameof(IMerchantPaymentProvider.GetPaymentAsync),
            nameof(IMerchantPaymentProvider.VerifyCheckoutSignature),
            nameof(IMerchantPaymentProvider.ProbeAsync)
        };

        foreach (var name in moneyMoving)
        {
            foreach (var method in typeof(IMerchantPaymentProvider).GetMethods().Where(m => m.Name == name))
            {
                method.GetParameters().First().ParameterType
                    .Should().Be<MerchantPaymentContext>($"{name} must say whose account it acts on");
            }
        }
    }

    // =====================================================================
    // 2. Credentials never reach a Payment row
    // =====================================================================

    [Fact]
    public void The_payment_row_holds_a_reference_to_a_connection_and_never_a_credential()
    {
        var credentialShaped = typeof(Payment)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name.Contains("Secret", StringComparison.OrdinalIgnoreCase) ||
                        p.Name.Contains("Cipher", StringComparison.OrdinalIgnoreCase) ||
                        p.Name.Contains("Token", StringComparison.OrdinalIgnoreCase) ||
                        p.Name.Contains("Password", StringComparison.OrdinalIgnoreCase) ||
                        p.Name.Contains("KeyId", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Name)
            .ToList();

        credentialShaped.Should().BeEmpty("a payment references the connection by id; it never copies its credentials");

        // The reference itself is present, so money stays traceable to the account holding it.
        typeof(Payment).GetProperty(nameof(Payment.MerchantConnectionId))!
            .PropertyType.Should().Be<Guid?>();
    }

    [Fact]
    public async Task A_real_payment_row_stores_no_credential_material()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client, 800m);
        var token = await CreateLinkAsync(client, invoice.Id);

        (await _factory.CreateClient().PostAsync($"/api/public/invoices/{token}/create-payment-order", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var payments = await _factory.ReadPaymentsAsync(invoice.Id);
        var payment = payments.Should().ContainSingle().Subject;

        // Serialised whole, so a credential hiding in any column would show up.
        var serialised = System.Text.Json.JsonSerializer.Serialize(payment);
        serialised.Should().NotContain(FakePaymentProvider.TestKeySecret);
        serialised.Should().NotContain(FakePaymentProvider.TestKeyId);
        serialised.Should().NotContain(_factory.ConnectionFor(client).WebhookSecret);

        payment.MerchantConnectionId.Should().NotBeNull();
    }

    // =====================================================================
    // 3. The provider account cross-check
    // =====================================================================

    [Fact]
    public async Task An_event_naming_a_different_razorpay_account_is_refused()
    {
        // Once the signature has passed, the payload is trustworthy enough to cross-check. If
        // Razorpay names an account, it must be the one this connection is for — a mismatch would
        // mean two connections share a secret, which should be impossible and is worth refusing.
        var client = await _factory.CreateSignedInClientAsync();
        var connection = _factory.ConnectionFor(client);
        var invoice = await CreateInvoiceAsync(client, 600m);
        var token = await CreateLinkAsync(client, invoice.Id);

        var order = (await (await _factory.CreateClient()
            .PostAsync($"/api/public/invoices/{token}/create-payment-order", null))
            .Content.ReadFromJsonAsync<PaymentOrderDto>(QuotelyApiFactory.Json))!;

        // Give this connection a known Razorpay account id, so the cross-check has something to
        // compare against. A key-pair connection has none by default: Razorpay does not return one.
        await SetProviderAccountIdAsync(client, "acc_this_merchant");

        var body = FakePaymentProvider.WebhookBody(
            "payment.captured", "pay_wrong_account", order.OrderId, "captured", order.Amount,
            accountId: "acc_a_completely_different_merchant");

        var request = new HttpRequestMessage(HttpMethod.Post, connection.WebhookUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add(
            "X-Razorpay-Signature", FakePaymentProvider.WebhookSignature(body, connection.WebhookSecret));
        request.Headers.Add("X-Razorpay-Event-Id", "evt_account_mismatch");

        var response = await _factory.CreateClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var summary = await client.GetFromJsonAsync<InvoicePaymentsDto>(
            $"/api/invoices/{invoice.Id}/payments", QuotelyApiFactory.Json);
        summary!.Summary.Paid.Should().Be(0m);
    }

    [Fact]
    public async Task A_matching_razorpay_account_is_accepted()
    {
        // The other half of the same rule: the cross-check must not reject a legitimate event.
        var client = await _factory.CreateSignedInClientAsync();
        var connection = _factory.ConnectionFor(client);
        var invoice = await CreateInvoiceAsync(client, 600m);
        var token = await CreateLinkAsync(client, invoice.Id);

        var order = (await (await _factory.CreateClient()
            .PostAsync($"/api/public/invoices/{token}/create-payment-order", null))
            .Content.ReadFromJsonAsync<PaymentOrderDto>(QuotelyApiFactory.Json))!;

        await SetProviderAccountIdAsync(client, "acc_this_merchant");

        var body = FakePaymentProvider.WebhookBody(
            "payment.captured", "pay_right_account", order.OrderId, "captured", order.Amount,
            accountId: "acc_this_merchant");

        var request = new HttpRequestMessage(HttpMethod.Post, connection.WebhookUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add(
            "X-Razorpay-Signature", FakePaymentProvider.WebhookSignature(body, connection.WebhookSecret));
        request.Headers.Add("X-Razorpay-Event-Id", "evt_account_match");

        (await _factory.CreateClient().SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.OK);

        var summary = await client.GetFromJsonAsync<InvoicePaymentsDto>(
            $"/api/invoices/{invoice.Id}/payments", QuotelyApiFactory.Json);
        summary!.Summary.Paid.Should().Be(600m);
    }

    // ---- helpers ---------------------------------------------------------

    /// <summary>
    /// Sets the connection's Razorpay account id directly. Razorpay supplies this during an OAuth
    /// exchange, which cannot run in the suite, so it is written here to exercise the cross-check
    /// that an OAuth connection would meet in production.
    /// </summary>
    private async Task SetProviderAccountIdAsync(HttpClient client, string accountId)
    {
        var webhookUrl = _factory.ConnectionFor(client).WebhookUrl;
        var routeToken = webhookUrl[(webhookUrl.LastIndexOf('/') + 1)..];

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var connection = await db.MerchantPaymentConnections
            .FirstAsync(c => c.WebhookRouteToken == routeToken);

        connection.ProviderAccountId = accountId;
        await db.SaveChangesAsync();
    }

    private static async Task<InvoiceDto> CreateInvoiceAsync(HttpClient client, decimal amount)
    {
        var customer = (await (await client.PostAsJsonAsync("/api/customers", new
        {
            name = "Paying Customer", email = "customer@example.com", city = "Chennai"
        })).Content.ReadFromJsonAsync<CustomerDto>(QuotelyApiFactory.Json))!;

        var response = await client.PostAsJsonAsync("/api/invoices", new
        {
            customerId = customer.Id,
            invoiceDate = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
            dueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)).ToString("yyyy-MM-dd"),
            items = new object[]
            {
                new { name = "Work done", unit = "Service", quantity = 1, unitPrice = amount, discount = 0, taxRate = 0 }
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<InvoiceDto>(QuotelyApiFactory.Json))!;
    }

    private static async Task<string> CreateLinkAsync(HttpClient client, Guid invoiceId)
    {
        var response = await client.PostAsync($"/api/invoices/{invoiceId}/public-link", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var link = (await response.Content.ReadFromJsonAsync<PublicInvoiceLinkDto>(QuotelyApiFactory.Json))!;
        return link.Url[(link.Url.LastIndexOf('/') + 1)..];
    }
}
