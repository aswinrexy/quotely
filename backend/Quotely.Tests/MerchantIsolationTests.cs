using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Quotely.Api.Data;
using Quotely.Api.DTOs;
using Quotely.Api.Models;
using Xunit;

namespace Quotely.Tests;

/// <summary>
/// The tests this milestone exists for.
///
/// Before V2.7, every business's invoice payments were collected into one Razorpay account — the
/// founder's. The money belonged to the businesses; the account did not. These tests assert the
/// property that fixes it, from as many directions as the API offers:
///
///   Business A's invoice is collected with Business A's credentials, and there is no request
///   Business B can make that changes that.
/// </summary>
public class MerchantIsolationTests : IClassFixture<QuotelyApiFactory>
{
    private readonly QuotelyApiFactory _factory;

    public MerchantIsolationTests(QuotelyApiFactory factory) => _factory = factory;

    // ---- setup ----------------------------------------------------------

    private static async Task<InvoiceDto> CreateInvoiceAsync(HttpClient client, decimal amount = 1000m)
    {
        var customer = (await (await client.PostAsJsonAsync("/api/customers", new
        {
            name = "Paying Customer",
            email = "customer@example.com",
            phone = "+91 91234 56780",
            city = "Chennai"
        })).Content.ReadFromJsonAsync<CustomerDto>(QuotelyApiFactory.Json))!;

        // No tax and a single unit, so the grand total is exactly the amount asked for — these
        // tests assert on precise paid figures and a rounded tax line would obscure them.
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

    private async Task<PaymentOrderDto> OrderAsync(string token)
    {
        var response = await _factory.CreateClient()
            .PostAsync($"/api/public/invoices/{token}/create-payment-order", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<PaymentOrderDto>(QuotelyApiFactory.Json))!;
    }

    private Task<HttpResponseMessage> DeliverAsync(
        string webhookUrl, string secret, string body, string eventId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, webhookUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Razorpay-Signature", FakePaymentProvider.WebhookSignature(body, secret));
        request.Headers.Add("X-Razorpay-Event-Id", eventId);
        return _factory.CreateClient().SendAsync(request);
    }

    // =====================================================================
    // 1. The money goes to the right account
    // =====================================================================

    [Fact]
    public async Task Each_businesss_invoice_is_collected_with_that_businesss_own_credentials()
    {
        var alice = await _factory.CreateSignedInClientAsync(connectPayments: false);
        await _factory.ConnectRazorpayAsync(alice, FakePaymentProvider.TestKeyId, FakePaymentProvider.TestKeySecret);

        var bob = await _factory.CreateSignedInClientAsync(connectPayments: false);
        await _factory.ConnectRazorpayAsync(bob, FakePaymentProvider.OtherKeyId, FakePaymentProvider.OtherKeySecret);

        var aliceOrder = await OrderAsync(await CreateLinkAsync(alice, (await CreateInvoiceAsync(alice)).Id));
        var bobOrder = await OrderAsync(await CreateLinkAsync(bob, (await CreateInvoiceAsync(bob)).Id));

        // The key the customer's browser is handed is the one belonging to the business that
        // issued the invoice — which is the whole milestone, expressed as one assertion.
        aliceOrder.KeyId.Should().Be(FakePaymentProvider.TestKeyId);
        bobOrder.KeyId.Should().Be(FakePaymentProvider.OtherKeyId);

        // And the order was genuinely created at the provider with those credentials, not merely
        // described that way in the response.
        _factory.Payments.OrderKeys[aliceOrder.OrderId].Should().Be(FakePaymentProvider.TestKeyId);
        _factory.Payments.OrderKeys[bobOrder.OrderId].Should().Be(FakePaymentProvider.OtherKeyId);
    }

    [Fact]
    public async Task An_invoice_cannot_be_paid_before_the_business_connects_an_account()
    {
        // Part 4 of the brief, enforced: no checkout until a valid connection exists. A business
        // with none is not quietly routed through somebody else's Razorpay.
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);
        var invoice = await CreateInvoiceAsync(client);
        var token = await CreateLinkAsync(client, invoice.Id);

        var response = await _factory.CreateClient()
            .PostAsync($"/api/public/invoices/{token}/create-payment-order", null);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        // The customer is told to contact the business, not shown an internal failure.
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("contact");
        body.Should().NotContain("Razorpay").And.NotContain("rzp_");
    }

    [Fact]
    public async Task The_public_invoice_page_offers_no_pay_button_without_a_connection()
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);
        var token = await CreateLinkAsync(client, (await CreateInvoiceAsync(client)).Id);

        var invoice = await _factory.CreateClient()
            .GetFromJsonAsync<PublicInvoiceDto>($"/api/public/invoices/{token}", QuotelyApiFactory.Json);

        // The balance is still shown; only the button that could not work is withheld.
        invoice!.CanPay.Should().BeFalse();
        invoice.GrandTotal.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Disconnecting_stops_new_payments_immediately()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var token = await CreateLinkAsync(client, (await CreateInvoiceAsync(client)).Id);

        (await _factory.CreateClient().PostAsync($"/api/public/invoices/{token}/create-payment-order", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await client.DeleteAsync("/api/settings/payments/razorpay")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await _factory.CreateClient().PostAsync($"/api/public/invoices/{token}/create-payment-order", null))
            .StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    // =====================================================================
    // 2. One merchant's webhook cannot touch another's invoice
    // =====================================================================

    [Fact]
    public async Task A_webhook_delivered_to_one_merchant_cannot_settle_another_merchants_invoice()
    {
        // THE attack this design exists to stop. Bob knows Alice's order id — it is visible to
        // anyone who opens her invoice link — and delivers a perfectly-signed capture for it to
        // his OWN webhook endpoint, using his OWN secret. Every signature check passes. The
        // payment must still not be credited, because the payment is not his.
        var alice = await _factory.CreateSignedInClientAsync(connectPayments: false);
        await _factory.ConnectRazorpayAsync(alice, FakePaymentProvider.TestKeyId, FakePaymentProvider.TestKeySecret);

        var bob = await _factory.CreateSignedInClientAsync(connectPayments: false);
        var bobConnection = await _factory.ConnectRazorpayAsync(
            bob, FakePaymentProvider.OtherKeyId, FakePaymentProvider.OtherKeySecret);

        var aliceInvoice = await CreateInvoiceAsync(alice, 2500m);
        var aliceOrder = await OrderAsync(await CreateLinkAsync(alice, aliceInvoice.Id));

        var body = FakePaymentProvider.WebhookBody(
            "payment.captured", "pay_stolen", aliceOrder.OrderId, "captured", aliceOrder.Amount);

        var response = await DeliverAsync(bobConnection.WebhookUrl, bobConnection.WebhookSecret, body, "evt_cross");

        // Acknowledged — the provider should stop retrying something we will never accept — but
        // Alice's invoice is untouched.
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var summary = await alice.GetFromJsonAsync<InvoicePaymentsDto>(
            $"/api/invoices/{aliceInvoice.Id}/payments", QuotelyApiFactory.Json);

        summary!.Summary.Paid.Should().Be(0m, "a payment belonging to Alice cannot be settled through Bob's account");
        summary.Summary.InvoiceStatus.Should().NotBe("Paid");
    }

    [Fact]
    public async Task A_webhook_signed_with_another_merchants_secret_is_rejected_at_the_door()
    {
        // The same delivery, at the right address but signed with the wrong merchant's secret.
        // Rejected outright: the signature is what authenticates, and it is per connection.
        var alice = await _factory.CreateSignedInClientAsync(connectPayments: false);
        var aliceConnection = await _factory.ConnectRazorpayAsync(
            alice, FakePaymentProvider.TestKeyId, FakePaymentProvider.TestKeySecret);

        var bob = await _factory.CreateSignedInClientAsync(connectPayments: false);
        var bobConnection = await _factory.ConnectRazorpayAsync(
            bob, FakePaymentProvider.OtherKeyId, FakePaymentProvider.OtherKeySecret);

        var order = await OrderAsync(await CreateLinkAsync(alice, (await CreateInvoiceAsync(alice)).Id));
        var body = FakePaymentProvider.WebhookBody(
            "payment.captured", "pay_wrong_secret", order.OrderId, "captured", order.Amount);

        var response = await DeliverAsync(
            aliceConnection.WebhookUrl, bobConnection.WebhookSecret, body, "evt_wrong_secret");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Two_merchants_can_use_the_same_provider_event_id_without_colliding()
    {
        // Razorpay numbers events per account, so two businesses can legitimately receive events
        // with the same id. A global idempotency key would treat the second as a duplicate and
        // silently discard a real payment.
        var alice = await _factory.CreateSignedInClientAsync(connectPayments: false);
        var aliceConnection = await _factory.ConnectRazorpayAsync(
            alice, FakePaymentProvider.TestKeyId, FakePaymentProvider.TestKeySecret);

        var bob = await _factory.CreateSignedInClientAsync(connectPayments: false);
        var bobConnection = await _factory.ConnectRazorpayAsync(
            bob, FakePaymentProvider.OtherKeyId, FakePaymentProvider.OtherKeySecret);

        var aliceInvoice = await CreateInvoiceAsync(alice, 900m);
        var bobInvoice = await CreateInvoiceAsync(bob, 700m);

        var aliceOrder = await OrderAsync(await CreateLinkAsync(alice, aliceInvoice.Id));
        var bobOrder = await OrderAsync(await CreateLinkAsync(bob, bobInvoice.Id));

        const string sharedEventId = "evt_they_both_call_it_this";

        await DeliverAsync(aliceConnection.WebhookUrl, aliceConnection.WebhookSecret,
            FakePaymentProvider.WebhookBody("payment.captured", "pay_a", aliceOrder.OrderId, "captured", aliceOrder.Amount),
            sharedEventId);

        await DeliverAsync(bobConnection.WebhookUrl, bobConnection.WebhookSecret,
            FakePaymentProvider.WebhookBody("payment.captured", "pay_b", bobOrder.OrderId, "captured", bobOrder.Amount),
            sharedEventId);

        (await alice.GetFromJsonAsync<InvoicePaymentsDto>(
            $"/api/invoices/{aliceInvoice.Id}/payments", QuotelyApiFactory.Json))!
            .Summary.Paid.Should().Be(900m);

        (await bob.GetFromJsonAsync<InvoicePaymentsDto>(
            $"/api/invoices/{bobInvoice.Id}/payments", QuotelyApiFactory.Json))!
            .Summary.Paid.Should().Be(700m, "Bob's event is not a duplicate of Alice's just because they share an id");
    }

    [Fact]
    public async Task A_redelivery_to_the_same_merchant_still_deduplicates()
    {
        // Scoping the key per connection must not weaken idempotency within one connection.
        var client = await _factory.CreateSignedInClientAsync();
        var connection = _factory.ConnectionFor(client);
        var invoice = await CreateInvoiceAsync(client, 1200m);
        var order = await OrderAsync(await CreateLinkAsync(client, invoice.Id));

        var body = FakePaymentProvider.WebhookBody(
            "payment.captured", "pay_redelivered", order.OrderId, "captured", order.Amount);

        for (var i = 0; i < 3; i++)
            (await DeliverAsync(connection.WebhookUrl, connection.WebhookSecret, body, "evt_same"))
                .StatusCode.Should().Be(HttpStatusCode.OK);

        var summary = await client.GetFromJsonAsync<InvoicePaymentsDto>(
            $"/api/invoices/{invoice.Id}/payments", QuotelyApiFactory.Json);

        summary!.Summary.Paid.Should().Be(1200m);
        summary.Payments.Should().HaveCount(1);
    }

    // =====================================================================
    // 3. Connection management
    // =====================================================================

    [Fact]
    public async Task One_business_cannot_see_or_alter_another_businesss_connection()
    {
        var alice = await _factory.CreateSignedInClientAsync();
        var bob = await _factory.CreateSignedInClientAsync(connectPayments: false);

        // There is no route that names a connection, so the only thing Bob can ask for is his
        // own — and his own is empty.
        var bobsView = await bob.GetFromJsonAsync<MerchantConnectionDto>(
            "/api/settings/payments/connection", QuotelyApiFactory.Json);

        bobsView!.Status.Should().Be(MerchantConnectionStatus.Disconnected);
        bobsView.AccountLabel.Should().BeNull();
        bobsView.CanAcceptPayments.Should().BeFalse();

        // Alice's is untouched by anything Bob did.
        var alicesView = await alice.GetFromJsonAsync<MerchantConnectionDto>(
            "/api/settings/payments/connection", QuotelyApiFactory.Json);

        alicesView!.Status.Should().Be(MerchantConnectionStatus.Connected);
    }

    [Fact]
    public async Task The_connection_endpoint_never_returns_a_stored_secret()
    {
        var client = await _factory.CreateSignedInClientAsync();

        var body = await (await client.GetAsync("/api/settings/payments/connection")).Content.ReadAsStringAsync();

        body.Should().NotContain(FakePaymentProvider.TestKeySecret);
        body.Should().NotContain(_factory.ConnectionFor(client).WebhookSecret);
        // The webhook secret is shown once, at creation, and never again.
        body.Should().Contain("\"webhookSecret\":null");
    }

    [Fact]
    public async Task The_key_secret_is_stored_encrypted_and_never_in_plaintext()
    {
        var client = await _factory.CreateSignedInClientAsync();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var connection = await db.MerchantPaymentConnections.AsNoTracking()
            .OrderByDescending(c => c.CreatedAt).FirstAsync();

        connection.KeySecretCipher.Should().NotBeNullOrWhiteSpace();
        connection.KeySecretCipher.Should().NotContain(FakePaymentProvider.TestKeySecret);
        connection.KeySecretCipher.Should().StartWith("v1.", "the stored form names the key that produced it");

        connection.WebhookSecretCipher.Should().NotBeNullOrWhiteSpace();
        connection.WebhookSecretCipher.Should().NotContain(_factory.ConnectionFor(client).WebhookSecret);

        // The publishable key is not a secret and is deliberately readable.
        connection.PublicKey.Should().Be(FakePaymentProvider.TestKeyId);
    }

    [Fact]
    public async Task Disconnecting_destroys_every_credential_rather_than_just_the_status()
    {
        var client = await _factory.CreateSignedInClientAsync();

        await client.DeleteAsync("/api/settings/payments/razorpay");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var connection = await db.MerchantPaymentConnections.AsNoTracking()
            .OrderByDescending(c => c.UpdatedAt).FirstAsync();

        connection.Status.Should().Be(MerchantConnectionStatus.Disconnected);
        connection.KeySecretCipher.Should().BeNull();
        connection.AccessTokenCipher.Should().BeNull();
        connection.RefreshTokenCipher.Should().BeNull();
        connection.WebhookSecretCipher.Should().BeNull();
        connection.PublicKey.Should().BeEmpty();
    }

    [Fact]
    public async Task Reconnecting_issues_a_new_webhook_address()
    {
        // A URL a merchant pasted somewhere before disconnecting must not address the connection
        // they make afterwards.
        var client = await _factory.CreateSignedInClientAsync();
        var before = _factory.ConnectionFor(client).WebhookUrl;

        await client.DeleteAsync("/api/settings/payments/razorpay");
        var after = (await _factory.ConnectRazorpayAsync(client)).WebhookUrl;

        after.Should().NotBe(before);
    }

    [Fact]
    public async Task A_rejected_key_is_reported_before_anything_is_stored()
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);
        _factory.Payments.RejectCredentials = true;

        try
        {
            var response = await client.PostAsJsonAsync("/api/settings/payments/razorpay/keys", new
            {
                keyId = FakePaymentProvider.TestKeyId,
                keySecret = "this_is_not_the_right_secret"
            });

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
        finally
        {
            _factory.Payments.RejectCredentials = false;
        }

        var view = await client.GetFromJsonAsync<MerchantConnectionDto>(
            "/api/settings/payments/connection", QuotelyApiFactory.Json);

        view!.Status.Should().NotBe(MerchantConnectionStatus.Connected);
        view.CanAcceptPayments.Should().BeFalse();
    }

    [Fact]
    public async Task A_live_key_is_refused_by_a_test_deployment()
    {
        // The environments have to agree. A live key here would collect real money through a
        // deployment nobody has signed off for it.
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);

        var response = await client.PostAsJsonAsync("/api/settings/payments/razorpay/keys", new
        {
            keyId = "rzp_live_something_real",
            keySecret = "a_live_secret"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("test mode");
    }

    [Fact]
    public async Task Something_that_is_not_a_razorpay_key_is_refused()
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);

        var response = await client.PostAsJsonAsync("/api/settings/payments/razorpay/keys", new
        {
            keyId = "sk_live_this_is_a_stripe_key",
            keySecret = "whatever"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Connecting_requires_a_signed_in_owner()
    {
        var anonymous = _factory.CreateClient();

        foreach (var response in new[]
        {
            await anonymous.GetAsync("/api/settings/payments/connection"),
            await anonymous.PostAsJsonAsync("/api/settings/payments/razorpay/keys",
                new { keyId = FakePaymentProvider.TestKeyId, keySecret = FakePaymentProvider.TestKeySecret }),
            await anonymous.PostAsync("/api/settings/payments/razorpay/oauth/start", null),
            await anonymous.DeleteAsync("/api/settings/payments/razorpay")
        })
        {
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
    }

    [Fact]
    public async Task Oauth_is_reported_unavailable_until_a_partner_application_is_configured()
    {
        // Quotely is not yet an approved Razorpay Technology Partner, so there is no client_id.
        // The honest answer is "not available", not a button that fails when pressed.
        var client = await _factory.CreateSignedInClientAsync();

        var view = await client.GetFromJsonAsync<MerchantConnectionDto>(
            "/api/settings/payments/connection", QuotelyApiFactory.Json);

        view!.OauthAvailable.Should().BeFalse();
        view.KeyPairAvailable.Should().BeTrue();

        var start = await client.PostAsync("/api/settings/payments/razorpay/oauth/start", null);
        start.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    // =====================================================================
    // 4. The payment record knows whose account holds the money
    // =====================================================================

    [Fact]
    public async Task A_payment_records_the_connection_that_collected_it()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client, 450m);
        var order = await OrderAsync(await CreateLinkAsync(client, invoice.Id));

        var payments = await _factory.ReadPaymentsAsync(invoice.Id);
        var payment = payments.Single(p => p.ProviderOrderId == order.OrderId);

        payment.MerchantConnectionId.Should().NotBeNull();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var connection = await db.MerchantPaymentConnections.AsNoTracking()
            .FirstAsync(c => c.Id == payment.MerchantConnectionId);

        connection.UserId.Should().Be(payment.UserId, "money is traceable to the account that holds it");
    }
}
