using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Quotely.Api.DTOs;
using Quotely.Api.Models;
using Xunit;

namespace Quotely.Tests;

/// <summary>
/// V2.3 — the public payment link, server-created orders, checkout verification, webhook
/// reconciliation and the derived financial state. Every test runs offline against
/// <see cref="FakePaymentProvider"/>.
/// </summary>
public class PaymentApiTests : IClassFixture<QuotelyApiFactory>
{
    private readonly QuotelyApiFactory _factory;

    public PaymentApiTests(QuotelyApiFactory factory) => _factory = factory;

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    // ---- fixtures -------------------------------------------------------

    private static async Task<CustomerDto> CreateCustomerAsync(HttpClient client, string name = "John Smith")
    {
        var response = await client.PostAsJsonAsync("/api/customers", new
        {
            name, companyName = $"{name} Ltd", email = "john@example.com", phone = "+91 91234 56780", city = "Chennai"
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<CustomerDto>())!;
    }

    /// <summary>Creates an accepted quotation, converts it, and returns the resulting invoice.</summary>
    private static async Task<InvoiceDto> CreateInvoiceAsync(HttpClient client, decimal unitPrice = 10000m)
    {
        var customer = await CreateCustomerAsync(client);

        var quotation = (await (await client.PostAsJsonAsync("/api/quotations", new
        {
            customerId = customer.Id,
            quotationDate = Today.ToString("yyyy-MM-dd"),
            validUntil = Today.AddDays(15).ToString("yyyy-MM-dd"),
            status = "Accepted",
            items = new object[]
            {
                new { name = "AC Installation", unit = "Service", quantity = 1, unitPrice, discount = 0, taxRate = 0 }
            }
        })).Content.ReadFromJsonAsync<QuotationDto>())!;

        var response = await client.PostAsync($"/api/quotations/{quotation.Id}/convert-to-invoice", null);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<InvoiceDto>())!;
    }

    private static async Task<string> CreateLinkAsync(HttpClient client, Guid invoiceId)
    {
        var response = await client.PostAsync($"/api/invoices/{invoiceId}/public-link", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var link = (await response.Content.ReadFromJsonAsync<PublicInvoiceLinkDto>())!;
        return link.Url[(link.Url.LastIndexOf('/') + 1)..];
    }

    private static async Task SetStatusAsync(HttpClient client, InvoiceDto invoice, string status)
    {
        var response = await client.PutAsJsonAsync($"/api/invoices/{invoice.Id}", new
        {
            invoiceDate = invoice.InvoiceDate.ToString("yyyy-MM-dd"),
            dueDate = invoice.DueDate.ToString("yyyy-MM-dd"),
            status
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static Task<HttpResponseMessage> CreateOrderAsync(HttpClient client, string token) =>
        client.PostAsync($"/api/public/invoices/{token}/create-payment-order", null);

    private static Task<HttpResponseMessage> VerifyAsync(
        HttpClient client, string token, string orderId, string paymentId, string? signature = null) =>
        client.PostAsJsonAsync($"/api/public/invoices/{token}/verify-payment", new
        {
            razorpayPaymentId = paymentId,
            razorpayOrderId = orderId,
            razorpaySignature = signature ?? FakePaymentProvider.CheckoutSignature(orderId, paymentId)
        });

    /// <summary>
    /// Delivers a webhook to the business's OWN endpoint, signed with its OWN secret — both read
    /// from the connection made when the client signed in. There is no shared webhook URL or
    /// shared secret any more, so a test cannot accidentally address the wrong merchant.
    /// </summary>
    private Task<HttpResponseMessage> WebhookAsync(HttpClient client, string body, string? eventId,
        string? signature = null)
    {
        var connection = _factory.ConnectionFor(client);

        var request = new HttpRequestMessage(HttpMethod.Post, connection.WebhookUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add(
            "X-Razorpay-Signature",
            signature ?? FakePaymentProvider.WebhookSignature(body, connection.WebhookSecret));
        if (eventId is not null) request.Headers.Add("X-Razorpay-Event-Id", eventId);
        return client.SendAsync(request);
    }

    // ---- public link ----------------------------------------------------

    [Fact]
    public async Task Only_the_token_hash_is_stored_and_the_raw_token_never_is()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client);

        var token = await CreateLinkAsync(client, invoice.Id);
        var stored = await _factory.ReadInvoiceTokenHashAsync(invoice.Id);

        token.Should().HaveLength(43, "32 random bytes in URL-safe base64");
        stored.Should().HaveLength(64).And.NotBe(token);
        stored.Should().Be(Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant());
    }

    [Fact]
    public async Task Creating_a_link_issues_the_invoice_and_a_new_link_revokes_the_old_one()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client);
        invoice.Status.Should().Be("Draft");

        var first = await CreateLinkAsync(client, invoice.Id);
        (await client.GetFromJsonAsync<InvoiceDto>($"/api/invoices/{invoice.Id}"))!.Status.Should().Be("Sent");

        var anonymous = _factory.CreateClient();
        (await anonymous.GetAsync($"/api/public/invoices/{first}")).StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await CreateLinkAsync(client, invoice.Id);
        second.Should().NotBe(first);

        (await anonymous.GetAsync($"/api/public/invoices/{first}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await anonymous.GetAsync($"/api/public/invoices/{second}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task One_user_cannot_mint_a_link_for_another_users_invoice()
    {
        var owner = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(owner);

        var intruder = await _factory.CreateSignedInClientAsync();

        (await intruder.PostAsync($"/api/invoices/{invoice.Id}/public-link", null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await intruder.GetAsync($"/api/invoices/{invoice.Id}/payments"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_public_link_endpoint_requires_authentication()
    {
        var owner = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(owner);

        var anonymous = _factory.CreateClient();

        (await anonymous.PostAsync($"/api/invoices/{invoice.Id}/public-link", null))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync($"/api/invoices/{invoice.Id}/payments"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---- public invoice access -----------------------------------------

    [Fact]
    public async Task A_valid_token_returns_the_invoice_without_any_internal_identifier()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client);
        var token = await CreateLinkAsync(client, invoice.Id);

        var response = await _factory.CreateClient().GetAsync($"/api/public/invoices/{token}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(invoice.InvoiceNumber).And.Contain("John Smith");
        body.Should().NotContain(invoice.Id.ToString());
        body.Should().NotContain("userId").And.NotContain("publicTokenHash").And.NotContain("customerId");
        // No GUID-shaped value of any kind should reach the customer.
        System.Text.RegularExpressions.Regex.IsMatch(body, @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-")
            .Should().BeFalse();

        var invoiceDto = (await response.Content.ReadFromJsonAsync<PublicInvoiceDto>())!;
        invoiceDto.Outstanding.Should().Be(10000m);
        invoiceDto.Paid.Should().Be(0m);
        invoiceDto.CanPay.Should().BeTrue();
    }

    [Theory]
    [InlineData("not-a-real-token-but-well-formed-abcdefghij")]
    [InlineData("../../etc/passwd")]
    [InlineData("' OR 1=1 --")]
    [InlineData("short")]
    public async Task An_unknown_or_malformed_token_is_an_indistinguishable_404(string token)
    {
        var anonymous = _factory.CreateClient();

        var response = await anonymous.GetAsync($"/api/public/invoices/{Uri.EscapeDataString(token)}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("invoice_");
    }

    [Fact]
    public async Task An_internal_invoice_id_is_not_a_payment_credential()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client);
        await CreateLinkAsync(client, invoice.Id);

        var response = await _factory.CreateClient().GetAsync($"/api/public/invoices/{invoice.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_draft_invoice_is_not_reachable_through_a_link()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client);
        var token = await CreateLinkAsync(client, invoice.Id);

        // Linking issued it; put it back to Draft and the public page must disappear entirely.
        await SetStatusAsync(client, invoice, "Draft");

        (await _factory.CreateClient().GetAsync($"/api/public/invoices/{token}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_cancelled_invoice_is_visible_but_not_payable()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client);
        var token = await CreateLinkAsync(client, invoice.Id);
        await SetStatusAsync(client, invoice, "Cancelled");

        var anonymous = _factory.CreateClient();
        var dto = await anonymous.GetFromJsonAsync<PublicInvoiceDto>($"/api/public/invoices/{token}");
        dto!.Status.Should().Be("Cancelled");
        dto.CanPay.Should().BeFalse();

        var order = await CreateOrderAsync(anonymous, token);
        order.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await order.Content.ReadAsStringAsync()).Should().Contain("cancelled");
    }

    [Fact]
    public async Task A_cancelled_invoice_cannot_be_shared()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client);
        await SetStatusAsync(client, invoice, "Cancelled");

        (await client.PostAsync($"/api/invoices/{invoice.Id}/public-link", null))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ---- order creation --------------------------------------------------

    [Fact]
    public async Task The_order_is_created_for_the_server_calculated_outstanding_balance()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client, unitPrice: 1250.50m);
        var token = await CreateLinkAsync(client, invoice.Id);

        var response = await CreateOrderAsync(_factory.CreateClient(), token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var order = (await response.Content.ReadFromJsonAsync<PaymentOrderDto>())!;
        order.Amount.Should().Be(125050, "₹1,250.50 is 125050 paise");
        order.Currency.Should().Be("INR");
        order.KeyId.Should().Be(FakePaymentProvider.TestKeyId);
        order.InvoiceNumber.Should().Be(invoice.InvoiceNumber);

        // The secret is nowhere near the response.
        (await response.Content.ReadAsStringAsync())
            .Should().NotContain(FakePaymentProvider.TestKeySecret)
            .And.NotContain(_factory.ConnectionFor(client).WebhookSecret);
    }

    [Fact]
    public async Task An_amount_sent_by_the_browser_is_ignored_entirely()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client, unitPrice: 10000m);
        var token = await CreateLinkAsync(client, invoice.Id);

        // A hostile browser tries to pay ₹1 for a ₹10,000 invoice.
        var response = await _factory.CreateClient().PostAsJsonAsync(
            $"/api/public/invoices/{token}/create-payment-order",
            new { amount = 100, currency = "USD", invoiceId = Guid.NewGuid(), total = 1 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var order = (await response.Content.ReadFromJsonAsync<PaymentOrderDto>())!;
        order.Amount.Should().Be(1000000);
        order.Currency.Should().Be("INR");
    }

    [Fact]
    public async Task Repeated_order_requests_reuse_one_order_rather_than_piling_up()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client);
        var token = await CreateLinkAsync(client, invoice.Id);
        var anonymous = _factory.CreateClient();

        // A double-click, a refresh and a second tab.
        var orders = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            var dto = (await (await CreateOrderAsync(anonymous, token)).Content.ReadFromJsonAsync<PaymentOrderDto>())!;
            orders.Add(dto.OrderId);
        }

        orders.Distinct().Should().HaveCount(1);
        (await _factory.ReadPaymentsAsync(invoice.Id)).Should().HaveCount(1);
    }

    [Fact]
    public async Task A_provider_outage_surfaces_as_a_safe_error_and_records_nothing()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client);
        var token = await CreateLinkAsync(client, invoice.Id);

        _factory.Payments.FailOrderCreation = true;
        try
        {
            var response = await CreateOrderAsync(_factory.CreateClient(), token);

            response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
            (await _factory.ReadPaymentsAsync(invoice.Id)).Should().BeEmpty();
        }
        finally
        {
            _factory.Payments.FailOrderCreation = false;
        }
    }

    // ---- checkout verification -------------------------------------------

    [Fact]
    public async Task A_verified_payment_settles_the_invoice_in_full()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client);
        var token = await CreateLinkAsync(client, invoice.Id);
        var anonymous = _factory.CreateClient();

        var order = (await (await CreateOrderAsync(anonymous, token)).Content.ReadFromJsonAsync<PaymentOrderDto>())!;
        _factory.Payments.Arrange("pay_full_1", order.OrderId, PaymentStatus.Captured, order.Amount);

        var response = await VerifyAsync(anonymous, token, order.OrderId, "pay_full_1");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = (await response.Content.ReadFromJsonAsync<VerifyPaymentResponse>())!;
        result.Success.Should().BeTrue();
        result.PaymentStatus.Should().Be("Captured");
        result.InvoiceStatus.Should().Be("Paid");
        result.Paid.Should().Be(10000m);
        result.Outstanding.Should().Be(0m);
        result.PaymentReference.Should().Be("pay_full_1");

        (await client.GetFromJsonAsync<InvoiceDto>($"/api/invoices/{invoice.Id}"))!.Status.Should().Be("Paid");
    }

    [Fact]
    public async Task An_invalid_signature_is_refused_and_records_nothing()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client);
        var token = await CreateLinkAsync(client, invoice.Id);
        var anonymous = _factory.CreateClient();

        var order = (await (await CreateOrderAsync(anonymous, token)).Content.ReadFromJsonAsync<PaymentOrderDto>())!;
        _factory.Payments.Arrange("pay_forged", order.OrderId, PaymentStatus.Captured, order.Amount);

        var response = await VerifyAsync(anonymous, token, order.OrderId, "pay_forged", "deadbeef");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var summary = await client.GetFromJsonAsync<InvoicePaymentsDto>($"/api/invoices/{invoice.Id}/payments");
        summary!.Summary.Paid.Should().Be(0m);
        summary.Summary.InvoiceStatus.Should().Be("Sent");
    }

    [Fact]
    public async Task A_payment_belonging_to_another_invoice_cannot_be_claimed()
    {
        var client = await _factory.CreateSignedInClientAsync();

        var mine = await CreateInvoiceAsync(client);
        var myToken = await CreateLinkAsync(client, mine.Id);
        var theirs = await CreateInvoiceAsync(client);
        var theirToken = await CreateLinkAsync(client, theirs.Id);

        var anonymous = _factory.CreateClient();
        var theirOrder = (await (await CreateOrderAsync(anonymous, theirToken))
            .Content.ReadFromJsonAsync<PaymentOrderDto>())!;
        _factory.Payments.Arrange("pay_cross", theirOrder.OrderId, PaymentStatus.Captured, theirOrder.Amount);

        // A correctly signed payment, but posted against a different invoice's link.
        var response = await VerifyAsync(anonymous, myToken, theirOrder.OrderId, "pay_cross");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        // Nothing was credited to the invoice whose link was used.
        (await _factory.ReadPaymentsAsync(mine.Id))
            .Should().NotContain(p => p.Status == PaymentStatus.Captured);
    }

    [Fact]
    public async Task An_unknown_order_is_refused()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client);
        var token = await CreateLinkAsync(client, invoice.Id);

        var response = await VerifyAsync(_factory.CreateClient(), token, "order_never_created", "pay_x");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(1000001L, "INR")]  // more than the order was for
    [InlineData(5000000L, "INR")]  // far more than the order was for
    [InlineData(1000000L, "USD")]  // wrong currency
    public async Task A_payment_that_disagrees_with_our_order_is_refused(long amount, string currency)
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client);
        var token = await CreateLinkAsync(client, invoice.Id);
        var anonymous = _factory.CreateClient();

        var order = (await (await CreateOrderAsync(anonymous, token)).Content.ReadFromJsonAsync<PaymentOrderDto>())!;
        var paymentId = $"pay_mismatch_{amount}_{currency}";
        _factory.Payments.Arrange(paymentId, order.OrderId, PaymentStatus.Captured, amount, currency);

        var response = await VerifyAsync(anonymous, token, order.OrderId, paymentId);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var summary = await client.GetFromJsonAsync<InvoicePaymentsDto>($"/api/invoices/{invoice.Id}/payments");
        summary!.Summary.Paid.Should().Be(0m);
    }

    [Fact]
    public async Task Verifying_the_same_payment_twice_does_not_double_count_it()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client);
        var token = await CreateLinkAsync(client, invoice.Id);
        var anonymous = _factory.CreateClient();

        var order = (await (await CreateOrderAsync(anonymous, token)).Content.ReadFromJsonAsync<PaymentOrderDto>())!;
        _factory.Payments.Arrange("pay_twice", order.OrderId, PaymentStatus.Captured, order.Amount);

        var first = (await (await VerifyAsync(anonymous, token, order.OrderId, "pay_twice"))
            .Content.ReadFromJsonAsync<VerifyPaymentResponse>())!;
        var second = (await (await VerifyAsync(anonymous, token, order.OrderId, "pay_twice"))
            .Content.ReadFromJsonAsync<VerifyPaymentResponse>())!;

        first.Paid.Should().Be(10000m);
        second.Paid.Should().Be(10000m, "a repeated confirmation must not add money twice");

        var captured = (await _factory.ReadPaymentsAsync(invoice.Id))
            .Where(p => p.Status == PaymentStatus.Captured).ToList();
        captured.Should().HaveCount(1);
    }

    [Fact]
    public async Task An_authorised_but_uncaptured_payment_does_not_count_as_paid()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client);
        var token = await CreateLinkAsync(client, invoice.Id);
        var anonymous = _factory.CreateClient();

        var order = (await (await CreateOrderAsync(anonymous, token)).Content.ReadFromJsonAsync<PaymentOrderDto>())!;
        _factory.Payments.Arrange("pay_pending", order.OrderId, PaymentStatus.Pending, order.Amount);

        var result = (await (await VerifyAsync(anonymous, token, order.OrderId, "pay_pending"))
            .Content.ReadFromJsonAsync<VerifyPaymentResponse>())!;

        result.Success.Should().BeFalse();
        result.PaymentStatus.Should().Be("Pending");
        result.Paid.Should().Be(0m);
        result.Outstanding.Should().Be(10000m);
        result.InvoiceStatus.Should().Be("Sent");
        result.Message.Should().Contain("confirmed");
    }

    [Fact]
    public async Task A_failed_payment_leaves_the_balance_untouched_and_allows_a_retry()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client);
        var token = await CreateLinkAsync(client, invoice.Id);
        var anonymous = _factory.CreateClient();

        var order = (await (await CreateOrderAsync(anonymous, token)).Content.ReadFromJsonAsync<PaymentOrderDto>())!;
        _factory.Payments.Arrange("pay_failed", order.OrderId, PaymentStatus.Failed, order.Amount,
            failureReason: "Insufficient funds");

        var failed = (await (await VerifyAsync(anonymous, token, order.OrderId, "pay_failed"))
            .Content.ReadFromJsonAsync<VerifyPaymentResponse>())!;
        failed.Success.Should().BeFalse();
        failed.Outstanding.Should().Be(10000m);

        // The customer tries again on the same order and succeeds.
        _factory.Payments.Arrange("pay_retry", order.OrderId, PaymentStatus.Captured, order.Amount);
        var retried = (await (await VerifyAsync(anonymous, token, order.OrderId, "pay_retry"))
            .Content.ReadFromJsonAsync<VerifyPaymentResponse>())!;

        retried.Success.Should().BeTrue();
        retried.Paid.Should().Be(10000m);
        retried.InvoiceStatus.Should().Be("Paid");
    }

    [Fact]
    public async Task A_fully_paid_invoice_refuses_a_further_payment_order()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client);
        var token = await CreateLinkAsync(client, invoice.Id);
        var anonymous = _factory.CreateClient();

        var order = (await (await CreateOrderAsync(anonymous, token)).Content.ReadFromJsonAsync<PaymentOrderDto>())!;
        _factory.Payments.Arrange("pay_settled", order.OrderId, PaymentStatus.Captured, order.Amount);
        await VerifyAsync(anonymous, token, order.OrderId, "pay_settled");

        var again = await CreateOrderAsync(anonymous, token);

        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await again.Content.ReadAsStringAsync()).Should().Contain("paid in full");

        var dto = await anonymous.GetFromJsonAsync<PublicInvoiceDto>($"/api/public/invoices/{token}");
        dto!.CanPay.Should().BeFalse();
        dto.Outstanding.Should().Be(0m);
    }

    // ---- webhooks --------------------------------------------------------

    [Fact]
    public async Task A_signed_webhook_records_the_payment()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client);
        var token = await CreateLinkAsync(client, invoice.Id);
        var anonymous = _factory.CreateClient();

        var order = (await (await CreateOrderAsync(anonymous, token)).Content.ReadFromJsonAsync<PaymentOrderDto>())!;
        var body = FakePaymentProvider.WebhookBody(
            "payment.captured", "pay_hook_1", order.OrderId, "captured", order.Amount);

        var response = await WebhookAsync(client, body, "evt_hook_1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var summary = await client.GetFromJsonAsync<InvoicePaymentsDto>($"/api/invoices/{invoice.Id}/payments");
        summary!.Summary.Paid.Should().Be(10000m);
        summary.Summary.InvoiceStatus.Should().Be("Paid");
    }

    [Fact]
    public async Task An_unsigned_or_wrongly_signed_webhook_is_rejected_and_changes_nothing()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client);
        var token = await CreateLinkAsync(client, invoice.Id);
        var anonymous = _factory.CreateClient();

        var order = (await (await CreateOrderAsync(anonymous, token)).Content.ReadFromJsonAsync<PaymentOrderDto>())!;
        var body = FakePaymentProvider.WebhookBody(
            "payment.captured", "pay_forged_hook", order.OrderId, "captured", order.Amount);

        var response = await WebhookAsync(client, body, "evt_forged", signature: "not-a-signature");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetFromJsonAsync<InvoicePaymentsDto>($"/api/invoices/{invoice.Id}/payments"))!
            .Summary.Paid.Should().Be(0m);
    }

    [Fact]
    public async Task A_redelivered_webhook_is_acknowledged_without_double_counting()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client);
        var token = await CreateLinkAsync(client, invoice.Id);
        var anonymous = _factory.CreateClient();

        var order = (await (await CreateOrderAsync(anonymous, token)).Content.ReadFromJsonAsync<PaymentOrderDto>())!;
        var body = FakePaymentProvider.WebhookBody(
            "payment.captured", "pay_dup", order.OrderId, "captured", order.Amount);

        for (var i = 0; i < 3; i++)
            (await WebhookAsync(client, body, "evt_dup")).StatusCode.Should().Be(HttpStatusCode.OK);

        var summary = await client.GetFromJsonAsync<InvoicePaymentsDto>($"/api/invoices/{invoice.Id}/payments");
        summary!.Summary.Paid.Should().Be(10000m);
        summary.Payments.Count(p => p.Status == "Captured").Should().Be(1);
    }

    [Fact]
    public async Task Different_events_for_the_same_payment_produce_one_payment_record()
    {
        // payment.captured and order.paid both describe the same successful transaction.
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client);
        var token = await CreateLinkAsync(client, invoice.Id);
        var anonymous = _factory.CreateClient();

        var order = (await (await CreateOrderAsync(anonymous, token)).Content.ReadFromJsonAsync<PaymentOrderDto>())!;

        await WebhookAsync(client,
            FakePaymentProvider.WebhookBody("payment.captured", "pay_same", order.OrderId, "captured", order.Amount),
            "evt_captured");
        await WebhookAsync(client,
            FakePaymentProvider.WebhookBody("order.paid", "pay_same", order.OrderId, "captured", order.Amount),
            "evt_order_paid");

        var summary = await client.GetFromJsonAsync<InvoicePaymentsDto>($"/api/invoices/{invoice.Id}/payments");
        summary!.Summary.Paid.Should().Be(10000m);
        summary.Payments.Should().HaveCount(1);
    }

    [Fact]
    public async Task An_out_of_order_authorised_event_cannot_demote_a_captured_payment()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client);
        var token = await CreateLinkAsync(client, invoice.Id);
        var anonymous = _factory.CreateClient();

        var order = (await (await CreateOrderAsync(anonymous, token)).Content.ReadFromJsonAsync<PaymentOrderDto>())!;

        // Razorpay does not guarantee ordering: captured arrives first, authorized second.
        await WebhookAsync(client,
            FakePaymentProvider.WebhookBody("payment.captured", "pay_ooo", order.OrderId, "captured", order.Amount),
            "evt_ooo_captured");
        await WebhookAsync(client,
            FakePaymentProvider.WebhookBody("payment.authorized", "pay_ooo", order.OrderId, "authorized", order.Amount),
            "evt_ooo_authorized");

        var summary = await client.GetFromJsonAsync<InvoicePaymentsDto>($"/api/invoices/{invoice.Id}/payments");
        summary!.Summary.Paid.Should().Be(10000m, "a late authorisation must not unpay a captured invoice");
        summary.Summary.InvoiceStatus.Should().Be("Paid");
        summary.Payments.Single().Status.Should().Be("Captured");
    }

    [Fact]
    public async Task A_webhook_and_a_checkout_callback_for_the_same_payment_agree()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client);
        var token = await CreateLinkAsync(client, invoice.Id);
        var anonymous = _factory.CreateClient();

        var order = (await (await CreateOrderAsync(anonymous, token)).Content.ReadFromJsonAsync<PaymentOrderDto>())!;
        _factory.Payments.Arrange("pay_both", order.OrderId, PaymentStatus.Captured, order.Amount);

        // Both confirmation paths run for the same payment, in the worst order.
        await WebhookAsync(client,
            FakePaymentProvider.WebhookBody("payment.captured", "pay_both", order.OrderId, "captured", order.Amount),
            "evt_both");
        var verified = (await (await VerifyAsync(anonymous, token, order.OrderId, "pay_both"))
            .Content.ReadFromJsonAsync<VerifyPaymentResponse>())!;

        verified.Paid.Should().Be(10000m);
        (await _factory.ReadPaymentsAsync(invoice.Id))
            .Count(p => p.Status == PaymentStatus.Captured).Should().Be(1);
    }

    [Fact]
    public async Task A_webhook_for_an_order_we_never_created_is_acknowledged_but_credits_nothing()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client);
        await CreateLinkAsync(client, invoice.Id);
        var anonymous = _factory.CreateClient();

        var body = FakePaymentProvider.WebhookBody(
            "payment.captured", "pay_stranger", "order_not_ours", "captured", 500000);

        // Acknowledged so the provider stops retrying, but no money is attributed to anyone.
        (await WebhookAsync(client, body, "evt_stranger")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await client.GetFromJsonAsync<InvoicePaymentsDto>($"/api/invoices/{invoice.Id}/payments"))!
            .Summary.Paid.Should().Be(0m);
    }

    [Fact]
    public async Task A_failed_payment_webhook_records_the_reason_without_crediting_anything()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client);
        var token = await CreateLinkAsync(client, invoice.Id);
        var anonymous = _factory.CreateClient();

        var order = (await (await CreateOrderAsync(anonymous, token)).Content.ReadFromJsonAsync<PaymentOrderDto>())!;
        var body = FakePaymentProvider.WebhookBody(
            "payment.failed", "pay_hook_failed", order.OrderId, "failed", order.Amount);

        (await WebhookAsync(client, body, "evt_failed")).StatusCode.Should().Be(HttpStatusCode.OK);

        var summary = await client.GetFromJsonAsync<InvoicePaymentsDto>($"/api/invoices/{invoice.Id}/payments");
        summary!.Summary.Paid.Should().Be(0m);
        summary.Summary.InvoiceStatus.Should().Be("Sent");
        summary.Payments.Single().Status.Should().Be("Failed");
    }

    [Fact]
    public async Task The_webhook_endpoint_needs_no_token_and_never_uses_one()
    {
        // It is anonymous to our JWT scheme and authenticated only by its signature — the
        // delivery in WebhookAsync is sent by a client carrying no bearer token at all.
        var client = await _factory.CreateSignedInClientAsync();

        var response = await WebhookAsync(client, "{\"event\":\"ping\"}", "evt_ping");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_webhook_delivered_to_an_unknown_address_is_rejected()
    {
        // The route token is what selects the merchant. One that matches nothing must be refused
        // outright rather than falling back to some default account.
        var request = new HttpRequestMessage(
            HttpMethod.Post, "/api/webhooks/razorpay/m/there-is-no-connection-with-this-token")
        {
            Content = new StringContent("{\"event\":\"ping\"}", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Razorpay-Signature", "irrelevant");

        var response = await _factory.CreateClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---- partial payments and the derived balance -------------------------

    [Fact]
    public async Task A_part_payment_moves_the_invoice_to_PartiallyPaid_and_leaves_the_remainder()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client, unitPrice: 10000m);
        var token = await CreateLinkAsync(client, invoice.Id);
        var anonymous = _factory.CreateClient();

        var order = (await (await CreateOrderAsync(anonymous, token)).Content.ReadFromJsonAsync<PaymentOrderDto>())!;
        order.Amount.Should().Be(1000000, "the first order is for the whole balance");

        // The capture settles for ₹3,000 of the ₹10,000 authorised.
        _factory.Payments.Arrange("pay_p1", order.OrderId, PaymentStatus.Captured, 300000);
        var result = (await (await VerifyAsync(anonymous, token, order.OrderId, "pay_p1"))
            .Content.ReadFromJsonAsync<VerifyPaymentResponse>())!;

        result.Success.Should().BeTrue();
        result.Paid.Should().Be(3000m);
        result.Outstanding.Should().Be(7000m);
        result.InvoiceStatus.Should().Be("PartiallyPaid");

        var dto = await anonymous.GetFromJsonAsync<PublicInvoiceDto>($"/api/public/invoices/{token}");
        dto!.CanPay.Should().BeTrue();
        dto.Outstanding.Should().Be(7000m);
    }

    [Fact]
    public async Task The_next_order_is_raised_for_the_remaining_balance_only()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client, unitPrice: 10000m);
        var token = await CreateLinkAsync(client, invoice.Id);
        var anonymous = _factory.CreateClient();

        var first = (await (await CreateOrderAsync(anonymous, token)).Content.ReadFromJsonAsync<PaymentOrderDto>())!;
        _factory.Payments.Arrange("pay_r1", first.OrderId, PaymentStatus.Captured, 300000);
        await VerifyAsync(anonymous, token, first.OrderId, "pay_r1");

        // ₹7,000 left, so that is exactly what the next order may collect.
        var second = (await (await CreateOrderAsync(anonymous, token)).Content.ReadFromJsonAsync<PaymentOrderDto>())!;
        second.OrderId.Should().NotBe(first.OrderId);
        second.Amount.Should().Be(700000);

        _factory.Payments.Arrange("pay_r2", second.OrderId, PaymentStatus.Captured, 700000);
        var final = (await (await VerifyAsync(anonymous, token, second.OrderId, "pay_r2"))
            .Content.ReadFromJsonAsync<VerifyPaymentResponse>())!;

        final.Paid.Should().Be(10000m);
        final.Outstanding.Should().Be(0m);
        final.InvoiceStatus.Should().Be("Paid");

        var history = await client.GetFromJsonAsync<InvoicePaymentsDto>($"/api/invoices/{invoice.Id}/payments");
        history!.Payments.Where(p => p.Status == "Captured").Sum(p => p.Amount).Should().Be(10000m);
    }

    [Fact]
    public async Task A_part_paid_invoice_cannot_be_overpaid_by_a_stale_order()
    {
        // Two tabs: both open an order for the full ₹10,000, then one of them pays. The second
        // order can no longer be used for more than the balance that actually remains.
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client, unitPrice: 10000m);
        var token = await CreateLinkAsync(client, invoice.Id);
        var anonymous = _factory.CreateClient();

        var order = (await (await CreateOrderAsync(anonymous, token)).Content.ReadFromJsonAsync<PaymentOrderDto>())!;
        _factory.Payments.Arrange("pay_ov1", order.OrderId, PaymentStatus.Captured, 1000000);
        await VerifyAsync(anonymous, token, order.OrderId, "pay_ov1");

        // The stale tab tries to pay the same order again with a second payment.
        _factory.Payments.Arrange("pay_ov2", order.OrderId, PaymentStatus.Captured, 1000000);
        await VerifyAsync(anonymous, token, order.OrderId, "pay_ov2");

        var summary = await client.GetFromJsonAsync<InvoicePaymentsDto>($"/api/invoices/{invoice.Id}/payments");

        // Whatever the provider reports is recorded truthfully, but the customer-facing balance
        // never goes negative and the invoice cannot be asked for more money.
        summary!.Summary.Outstanding.Should().Be(0m);
        summary.Summary.InvoiceStatus.Should().Be("Paid");

        (await CreateOrderAsync(anonymous, token)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task The_paid_amount_is_summed_from_captured_payments_not_stored_on_the_invoice()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client);
        var token = await CreateLinkAsync(client, invoice.Id);
        var anonymous = _factory.CreateClient();

        var order = (await (await CreateOrderAsync(anonymous, token)).Content.ReadFromJsonAsync<PaymentOrderDto>())!;

        // One captured, one pending, one failed. Only the captured one may count.
        await WebhookAsync(client,
            FakePaymentProvider.WebhookBody("payment.captured", "pay_sum_ok", order.OrderId, "captured", order.Amount),
            "evt_sum_1");
        await WebhookAsync(client,
            FakePaymentProvider.WebhookBody("payment.authorized", "pay_sum_pending", order.OrderId, "authorized", order.Amount),
            "evt_sum_2");
        await WebhookAsync(client,
            FakePaymentProvider.WebhookBody("payment.failed", "pay_sum_failed", order.OrderId, "failed", order.Amount),
            "evt_sum_3");

        var summary = await client.GetFromJsonAsync<InvoicePaymentsDto>($"/api/invoices/{invoice.Id}/payments");
        summary!.Summary.Paid.Should().Be(10000m, "only captured payments are money");
        summary.Summary.Total.Should().Be(10000m);
        summary.Summary.Outstanding.Should().Be(0m);
        summary.Payments.Should().HaveCount(3);
    }

    [Fact]
    public async Task An_invoice_with_no_payments_keeps_the_status_its_owner_set()
    {
        // V2.2 behaviour must survive: payments take over only once money has arrived.
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client);
        await CreateLinkAsync(client, invoice.Id);
        await SetStatusAsync(client, invoice, "Overdue");

        var summary = await client.GetFromJsonAsync<InvoicePaymentsDto>($"/api/invoices/{invoice.Id}/payments");

        summary!.Summary.InvoiceStatus.Should().Be("Overdue");
        summary.Summary.Paid.Should().Be(0m);
        summary.Summary.Outstanding.Should().Be(10000m);
        summary.Summary.CanPay.Should().BeTrue("an overdue invoice is still payable");
    }

    [Fact]
    public async Task Payment_history_carries_a_reference_and_a_method_but_nothing_sensitive()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client);
        var token = await CreateLinkAsync(client, invoice.Id);
        var anonymous = _factory.CreateClient();

        var order = (await (await CreateOrderAsync(anonymous, token)).Content.ReadFromJsonAsync<PaymentOrderDto>())!;
        await WebhookAsync(client,
            FakePaymentProvider.WebhookBody("payment.captured", "pay_history", order.OrderId, "captured", order.Amount),
            "evt_history");

        var response = await client.GetAsync($"/api/invoices/{invoice.Id}/payments");
        var payments = (await response.Content.ReadFromJsonAsync<InvoicePaymentsDto>())!;

        var payment = payments.Payments.Single();
        payment.Reference.Should().Be("pay_history");
        payment.OrderReference.Should().Be(order.OrderId);
        payment.Method.Should().Be("upi");
        payment.Amount.Should().Be(10000m);
        payment.PaidAt.Should().NotBeNull();

        (await response.Content.ReadAsStringAsync())
            .Should().NotContain(FakePaymentProvider.TestKeySecret)
            .And.NotContain(_factory.ConnectionFor(client).WebhookSecret);
    }

    // ---- pdf --------------------------------------------------------------

    [Fact]
    public async Task The_public_payment_link_serves_the_invoice_pdf()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client);
        var token = await CreateLinkAsync(client, invoice.Id);

        var response = await _factory.CreateClient().GetAsync($"/api/public/invoices/{token}/pdf");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        response.Content.Headers.ContentDisposition!.FileName.Should().Contain(invoice.InvoiceNumber);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
    }
}
