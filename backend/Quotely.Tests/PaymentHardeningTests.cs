using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Quotely.Api.DTOs;
using Quotely.Api.Models;
using Xunit;

namespace Quotely.Tests;

/// <summary>
/// V2.3 hardening: the concurrency, ordering and amount-integrity properties the payment flow has
/// to hold even when browsers misbehave and webhooks arrive twice, late, or out of order.
/// </summary>
public class PaymentHardeningTests : IClassFixture<QuotelyApiFactory>
{
    private readonly QuotelyApiFactory _factory;

    public PaymentHardeningTests(QuotelyApiFactory factory) => _factory = factory;

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    // ---- fixtures -------------------------------------------------------

    private static async Task<InvoiceDto> CreateInvoiceAsync(HttpClient client, decimal unitPrice = 10000m)
    {
        var customer = (await (await client.PostAsJsonAsync("/api/customers", new
        {
            name = "John Smith", email = "john@example.com", phone = "+91 91234 56780"
        })).Content.ReadFromJsonAsync<CustomerDto>())!;

        var quotation = (await (await client.PostAsJsonAsync("/api/quotations", new
        {
            customerId = customer.Id,
            quotationDate = Today.ToString("yyyy-MM-dd"),
            validUntil = Today.AddDays(15).ToString("yyyy-MM-dd"),
            status = "Accepted",
            items = new object[]
            {
                new { name = "Service", unit = "Service", quantity = 1, unitPrice, discount = 0, taxRate = 0 }
            }
        })).Content.ReadFromJsonAsync<QuotationDto>())!;

        return (await (await client.PostAsync($"/api/quotations/{quotation.Id}/convert-to-invoice", null))
            .Content.ReadFromJsonAsync<InvoiceDto>())!;
    }

    private static async Task<string> CreateLinkAsync(HttpClient client, Guid invoiceId)
    {
        var link = (await (await client.PostAsync($"/api/invoices/{invoiceId}/public-link", null))
            .Content.ReadFromJsonAsync<PublicInvoiceLinkDto>())!;
        return link.Url[(link.Url.LastIndexOf('/') + 1)..];
    }

    private static Task<HttpResponseMessage> CreateOrderAsync(HttpClient client, string token) =>
        client.PostAsync($"/api/public/invoices/{token}/create-payment-order", null);

    private static async Task<PaymentOrderDto> OrderAsync(HttpClient client, string token)
    {
        var response = await CreateOrderAsync(client, token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<PaymentOrderDto>())!;
    }

    private static Task<HttpResponseMessage> VerifyAsync(
        HttpClient client, string token, string orderId, string paymentId, string? signature = null) =>
        client.PostAsJsonAsync($"/api/public/invoices/{token}/verify-payment", new
        {
            razorpayPaymentId = paymentId,
            razorpayOrderId = orderId,
            razorpaySignature = signature ?? FakePaymentProvider.CheckoutSignature(orderId, paymentId)
        });

    private static Task<HttpResponseMessage> WebhookAsync(
        HttpClient client, string body, string? eventId, string? signature = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/razorpay")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Razorpay-Signature", signature ?? FakePaymentProvider.WebhookSignature(body));
        if (eventId is not null) request.Headers.Add("X-Razorpay-Event-Id", eventId);
        return client.SendAsync(request);
    }

    private Task<InvoicePaymentsDto?> SummaryAsync(HttpClient owner, Guid invoiceId) =>
        owner.GetFromJsonAsync<InvoicePaymentsDto>($"/api/invoices/{invoiceId}/payments");

    // =====================================================================
    // 1. Concurrent payment attempts must not both consume the balance
    // =====================================================================

    [Fact]
    public async Task Two_simultaneous_payment_attempts_cannot_both_reserve_the_same_balance()
    {
        var owner = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(owner, 5000m);
        var token = await CreateLinkAsync(owner, invoice.Id);

        // Two tabs, fired together, each with its own connection.
        var tabs = Enumerable.Range(0, 6).Select(_ => _factory.CreateClient()).ToList();
        var responses = await Task.WhenAll(tabs.Select(tab => CreateOrderAsync(tab, token)));

        var orders = new List<string>();
        foreach (var response in responses)
        {
            if (response.StatusCode != HttpStatusCode.OK) continue;
            orders.Add((await response.Content.ReadFromJsonAsync<PaymentOrderDto>())!.OrderId);
        }

        // Every tab that got an order got the *same* one, so only one balance was reserved.
        orders.Should().NotBeEmpty();
        orders.Distinct().Should().ContainSingle("the invoice's balance may only be reserved once");

        // And exactly one live reservation exists in the database.
        var rows = await _factory.ReadPaymentsAsync(invoice.Id);
        rows.Count(p => p.ReservationSlot is not null).Should().Be(1);
    }

    [Fact]
    public async Task A_live_reservation_blocks_a_second_order_for_a_different_amount()
    {
        var owner = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(owner, 10000m);
        var token = await CreateLinkAsync(owner, invoice.Id);
        var anonymous = _factory.CreateClient();

        var first = await OrderAsync(anonymous, token);

        // An authorised payment is money the provider is holding. Starting a second attempt is
        // exactly how an invoice would get paid twice, so it is refused.
        _factory.Payments.Arrange("pay_auth_hold", first.OrderId, PaymentStatus.Pending, first.Amount);
        await VerifyAsync(anonymous, token, first.OrderId, "pay_auth_hold");

        var second = await CreateOrderAsync(anonymous, token);

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await second.Content.ReadAsStringAsync()).Should().Contain("being confirmed");
    }

    [Fact]
    public async Task A_settled_attempt_releases_its_reservation()
    {
        var owner = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(owner, 10000m);
        var token = await CreateLinkAsync(owner, invoice.Id);
        var anonymous = _factory.CreateClient();

        var order = await OrderAsync(anonymous, token);
        (await _factory.ReadPaymentsAsync(invoice.Id)).Should().ContainSingle()
            .Which.ReservationSlot.Should().NotBeNull();

        // A failed attempt frees the invoice to be paid again.
        _factory.Payments.Arrange("pay_rel_failed", order.OrderId, PaymentStatus.Failed, order.Amount);
        await VerifyAsync(anonymous, token, order.OrderId, "pay_rel_failed");

        (await _factory.ReadPaymentsAsync(invoice.Id))
            .Should().OnlyContain(p => p.ReservationSlot == null);

        var retry = await OrderAsync(anonymous, token);
        retry.Amount.Should().Be(1000000, "the whole balance is available again");
    }

    [Fact]
    public async Task A_part_payment_retires_a_stale_order_and_reprices_the_next_one()
    {
        var owner = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(owner, 10000m);
        var token = await CreateLinkAsync(owner, invoice.Id);
        var anonymous = _factory.CreateClient();

        var first = await OrderAsync(anonymous, token);
        _factory.Payments.Arrange("pay_stale_part", first.OrderId, PaymentStatus.Captured, 300000);
        await VerifyAsync(anonymous, token, first.OrderId, "pay_stale_part");

        var second = await OrderAsync(anonymous, token);

        second.OrderId.Should().NotBe(first.OrderId);
        second.Amount.Should().Be(700000, "only the remaining balance may be reserved");

        (await _factory.ReadPaymentsAsync(invoice.Id))
            .Count(p => p.ReservationSlot is not null).Should().Be(1);
    }

    // =====================================================================
    // 2. Overpayment policy
    // =====================================================================

    [Fact]
    public async Task An_unexpected_excess_capture_is_recorded_truthfully_and_reported_as_an_anomaly()
    {
        var owner = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(owner, 10000m);
        var token = await CreateLinkAsync(owner, invoice.Id);
        var anonymous = _factory.CreateClient();

        var order = await OrderAsync(anonymous, token);

        // Two captures land on the same order — a provider-side condition our flow cannot cause,
        // but which must not corrupt the ledger if it happens.
        await WebhookAsync(anonymous,
            FakePaymentProvider.WebhookBody("payment.captured", "pay_ex_1", order.OrderId, "captured", 1000000),
            "evt_ex_1");
        await WebhookAsync(anonymous,
            FakePaymentProvider.WebhookBody("payment.captured", "pay_ex_2", order.OrderId, "captured", 1000000),
            "evt_ex_2");

        var summary = (await SummaryAsync(owner, invoice.Id))!.Summary;

        summary.Paid.Should().Be(20000m, "captured money is never discarded or quietly altered");
        summary.Outstanding.Should().Be(0m, "a customer must never be shown a negative balance");
        summary.OverpaidBy.Should().Be(10000m, "the excess is surfaced for a human to resolve");
        summary.InvoiceStatus.Should().Be("Paid");
        summary.CanPay.Should().BeFalse();
    }

    [Fact]
    public async Task An_exactly_settled_invoice_reports_no_anomaly()
    {
        var owner = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(owner, 10000m);
        var token = await CreateLinkAsync(owner, invoice.Id);
        var anonymous = _factory.CreateClient();

        var order = await OrderAsync(anonymous, token);
        _factory.Payments.Arrange("pay_exact", order.OrderId, PaymentStatus.Captured, order.Amount);
        await VerifyAsync(anonymous, token, order.OrderId, "pay_exact");

        var summary = (await SummaryAsync(owner, invoice.Id))!.Summary;
        summary.OverpaidBy.Should().Be(0m);
        summary.Outstanding.Should().Be(0m);
        summary.InvoiceStatus.Should().Be("Paid");
    }

    // =====================================================================
    // 3. order.paid and payment.captured
    // =====================================================================

    private static string Captured(string paymentId, string orderId, long amount) =>
        FakePaymentProvider.WebhookBody("payment.captured", paymentId, orderId, "captured", amount);

    private static string OrderPaid(string paymentId, string orderId, long amount) =>
        FakePaymentProvider.WebhookBody("order.paid", paymentId, orderId, "captured", amount);

    [Fact]
    public async Task Captured_then_order_paid_yields_exactly_one_payment()
    {
        var (owner, invoice, token, anonymous, order) = await ReadyToPayAsync();

        await WebhookAsync(anonymous, Captured("pay_c_then_o", order.OrderId, order.Amount), "evt_c1");
        await WebhookAsync(anonymous, OrderPaid("pay_c_then_o", order.OrderId, order.Amount), "evt_o1");

        await AssertSinglePaymentAsync(owner, invoice.Id);
    }

    [Fact]
    public async Task Order_paid_then_captured_yields_exactly_one_payment()
    {
        var (owner, invoice, token, anonymous, order) = await ReadyToPayAsync();

        // order.paid arriving first is safe because its payload names the actual payment, so the
        // unique ProviderPaymentId still collapses the pair onto one record.
        await WebhookAsync(anonymous, OrderPaid("pay_o_then_c", order.OrderId, order.Amount), "evt_o2");
        await WebhookAsync(anonymous, Captured("pay_o_then_c", order.OrderId, order.Amount), "evt_c2");

        await AssertSinglePaymentAsync(owner, invoice.Id);
    }

    [Fact]
    public async Task A_duplicated_order_paid_yields_exactly_one_payment()
    {
        var (owner, invoice, token, anonymous, order) = await ReadyToPayAsync();

        // Same event id — the provider redelivering — and then a different event id carrying the
        // same payment, which the payment-id constraint catches instead.
        await WebhookAsync(anonymous, OrderPaid("pay_dup_o", order.OrderId, order.Amount), "evt_o3");
        await WebhookAsync(anonymous, OrderPaid("pay_dup_o", order.OrderId, order.Amount), "evt_o3");
        await WebhookAsync(anonymous, OrderPaid("pay_dup_o", order.OrderId, order.Amount), "evt_o3_again");

        await AssertSinglePaymentAsync(owner, invoice.Id);
    }

    [Fact]
    public async Task A_duplicated_payment_captured_yields_exactly_one_payment()
    {
        var (owner, invoice, token, anonymous, order) = await ReadyToPayAsync();

        await WebhookAsync(anonymous, Captured("pay_dup_c", order.OrderId, order.Amount), "evt_c3");
        await WebhookAsync(anonymous, Captured("pay_dup_c", order.OrderId, order.Amount), "evt_c3");
        await WebhookAsync(anonymous, Captured("pay_dup_c", order.OrderId, order.Amount), "evt_c3_again");

        await AssertSinglePaymentAsync(owner, invoice.Id);
    }

    [Fact]
    public async Task An_order_paid_event_without_a_payment_entity_creates_nothing()
    {
        var (owner, invoice, _, anonymous, order) = await ReadyToPayAsync();

        // An order-level signal alone gives us no payment id to deduplicate on, so we refuse to
        // invent a successful payment from it and wait for payment.captured instead.
        var body = "{\"event\":\"order.paid\",\"payload\":{\"order\":{\"entity\":{\"id\":\"" +
                   order.OrderId + "\",\"status\":\"paid\",\"amount\":" + order.Amount + "}}}}";

        (await WebhookAsync(anonymous, body, "evt_order_only")).StatusCode.Should().Be(HttpStatusCode.OK);

        var summary = (await SummaryAsync(owner, invoice.Id))!;
        summary.Summary.Paid.Should().Be(0m);
        summary.Payments.Should().BeEmpty();
    }

    private async Task<(HttpClient Owner, InvoiceDto Invoice, string Token, HttpClient Anonymous, PaymentOrderDto Order)>
        ReadyToPayAsync(decimal amount = 10000m)
    {
        var owner = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(owner, amount);
        var token = await CreateLinkAsync(owner, invoice.Id);
        var anonymous = _factory.CreateClient();
        var order = await OrderAsync(anonymous, token);
        return (owner, invoice, token, anonymous, order);
    }

    private async Task AssertSinglePaymentAsync(HttpClient owner, Guid invoiceId)
    {
        var summary = (await SummaryAsync(owner, invoiceId))!;
        summary.Payments.Count(p => p.Status == "Captured").Should().Be(1);
        summary.Summary.Paid.Should().Be(10000m);
        summary.Summary.Outstanding.Should().Be(0m);
        summary.Summary.OverpaidBy.Should().Be(0m);
        summary.Summary.InvoiceStatus.Should().Be("Paid");
    }

    // =====================================================================
    // 4. Payment state machine
    // =====================================================================

    [Fact]
    public async Task A_captured_payment_can_never_be_walked_back_to_pending_or_failed()
    {
        var (owner, invoice, _, anonymous, order) = await ReadyToPayAsync();

        await WebhookAsync(anonymous, Captured("pay_sm", order.OrderId, order.Amount), "evt_sm_1");

        // Both demotions arrive late, as Razorpay's unordered delivery permits.
        await WebhookAsync(anonymous,
            FakePaymentProvider.WebhookBody("payment.authorized", "pay_sm", order.OrderId, "authorized", order.Amount),
            "evt_sm_2");
        await WebhookAsync(anonymous,
            FakePaymentProvider.WebhookBody("payment.failed", "pay_sm", order.OrderId, "failed", order.Amount),
            "evt_sm_3");

        var summary = (await SummaryAsync(owner, invoice.Id))!;
        summary.Payments.Single().Status.Should().Be("Captured");
        summary.Summary.Paid.Should().Be(10000m);
        summary.Summary.InvoiceStatus.Should().Be("Paid");
    }

    [Fact]
    public async Task A_failed_payment_that_the_provider_later_captures_does_become_captured()
    {
        // The legitimate forward transition: same payment id, failed then captured.
        var (owner, invoice, _, anonymous, order) = await ReadyToPayAsync();

        await WebhookAsync(anonymous,
            FakePaymentProvider.WebhookBody("payment.failed", "pay_recover", order.OrderId, "failed", order.Amount),
            "evt_rec_1");

        (await SummaryAsync(owner, invoice.Id))!.Summary.Paid.Should().Be(0m);

        await WebhookAsync(anonymous, Captured("pay_recover", order.OrderId, order.Amount), "evt_rec_2");

        var summary = (await SummaryAsync(owner, invoice.Id))!;
        summary.Payments.Single().Status.Should().Be("Captured");
        summary.Payments.Single().FailureReason.Should().BeNull("the stale failure reason is cleared");
        summary.Summary.Paid.Should().Be(10000m);
    }

    [Fact]
    public async Task An_authorised_payment_can_progress_to_captured()
    {
        var (owner, invoice, _, anonymous, order) = await ReadyToPayAsync();

        await WebhookAsync(anonymous,
            FakePaymentProvider.WebhookBody("payment.authorized", "pay_prog", order.OrderId, "authorized", order.Amount),
            "evt_prog_1");

        var held = (await SummaryAsync(owner, invoice.Id))!.Summary;
        held.Paid.Should().Be(0m, "authorised money is not captured money");
        held.HasPendingPayment.Should().BeTrue();

        await WebhookAsync(anonymous, Captured("pay_prog", order.OrderId, order.Amount), "evt_prog_2");

        var settled = (await SummaryAsync(owner, invoice.Id))!.Summary;
        settled.Paid.Should().Be(10000m);
        settled.HasPendingPayment.Should().BeFalse();
    }

    // =====================================================================
    // 5. Amount integrity — what the browser may and may not influence
    // =====================================================================

    [Fact]
    public async Task No_field_the_browser_sends_can_change_what_is_charged()
    {
        var owner = await _factory.CreateSignedInClientAsync();
        var mine = await CreateInvoiceAsync(owner, 10000m);
        var other = await CreateInvoiceAsync(owner, 250m);
        var token = await CreateLinkAsync(owner, mine.Id);

        var response = await _factory.CreateClient().PostAsJsonAsync(
            $"/api/public/invoices/{token}/create-payment-order",
            new
            {
                amount = 1,
                amountInMinorUnits = 1,
                currency = "USD",
                invoiceId = other.Id,
                invoiceNumber = other.InvoiceNumber,
                outstanding = 1,
                total = 1,
                status = "Paid",
                paid = 10000
            });

        var order = (await response.Content.ReadFromJsonAsync<PaymentOrderDto>())!;
        order.Amount.Should().Be(1000000, "the amount comes from the invoice the token names");
        order.Currency.Should().Be("INR");
        order.InvoiceNumber.Should().Be(mine.InvoiceNumber);

        // The other invoice was untouched.
        (await SummaryAsync(owner, other.Id))!.Summary.Paid.Should().Be(0m);
    }

    [Fact]
    public async Task The_browser_cannot_claim_a_payment_was_captured()
    {
        var (owner, invoice, token, anonymous, order) = await ReadyToPayAsync();

        // A correctly signed callback for a payment the provider says merely failed.
        _factory.Payments.Arrange("pay_liar", order.OrderId, PaymentStatus.Failed, order.Amount);

        var result = (await (await VerifyAsync(anonymous, token, order.OrderId, "pay_liar"))
            .Content.ReadFromJsonAsync<VerifyPaymentResponse>())!;

        result.Success.Should().BeFalse("the provider decides the outcome, not the caller");
        result.PaymentStatus.Should().Be("Failed");
        (await SummaryAsync(owner, invoice.Id))!.Summary.Paid.Should().Be(0m);
    }

    [Fact]
    public async Task A_mismatched_payment_and_order_pairing_is_refused()
    {
        var (owner, invoice, token, anonymous, order) = await ReadyToPayAsync();

        // The signature is valid for the submitted pairing, but the provider reports this payment
        // against a different order. The corroboration step catches it.
        _factory.Payments.Arrange("pay_swapped", "order_somewhere_else", PaymentStatus.Captured, order.Amount);

        var response = await VerifyAsync(anonymous, token, order.OrderId, "pay_swapped");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await SummaryAsync(owner, invoice.Id))!.Summary.Paid.Should().Be(0m);
    }

    // =====================================================================
    // 6 & 9. Public token and tenant isolation
    // =====================================================================

    [Fact]
    public async Task A_public_token_is_not_a_credential_for_owner_endpoints()
    {
        var owner = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(owner);
        var token = await CreateLinkAsync(owner, invoice.Id);

        var impostor = _factory.CreateClient();
        impostor.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // The share token is a capability for one public page, not a session.
        (await impostor.GetAsync($"/api/invoices/{invoice.Id}/payments"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await impostor.PostAsync($"/api/invoices/{invoice.Id}/public-link", null))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await impostor.GetAsync("/api/invoices")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task One_businesss_token_cannot_reach_another_businesss_invoice()
    {
        var first = await _factory.CreateSignedInClientAsync();
        var theirs = await CreateInvoiceAsync(first, 10000m);
        var theirToken = await CreateLinkAsync(first, theirs.Id);

        var second = await _factory.CreateSignedInClientAsync();
        var mine = await CreateInvoiceAsync(second, 500m);
        var myToken = await CreateLinkAsync(second, mine.Id);

        var anonymous = _factory.CreateClient();

        // Each token resolves to precisely one invoice, and never the other business's.
        (await anonymous.GetFromJsonAsync<PublicInvoiceDto>($"/api/public/invoices/{theirToken}"))!
            .InvoiceNumber.Should().Be(theirs.InvoiceNumber);
        (await anonymous.GetFromJsonAsync<PublicInvoiceDto>($"/api/public/invoices/{myToken}"))!
            .GrandTotal.Should().Be(500m);

        // Cross-tenant owner access stays closed.
        (await second.GetAsync($"/api/invoices/{theirs.Id}/payments"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await second.PostAsync($"/api/invoices/{theirs.Id}/public-link", null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_webhook_cannot_move_money_between_invoices()
    {
        var owner = await _factory.CreateSignedInClientAsync();
        var paying = await CreateInvoiceAsync(owner, 10000m);
        var bystander = await CreateInvoiceAsync(owner, 10000m);
        var token = await CreateLinkAsync(owner, paying.Id);
        await CreateLinkAsync(owner, bystander.Id);

        var anonymous = _factory.CreateClient();
        var order = await OrderAsync(anonymous, token);

        await WebhookAsync(anonymous, Captured("pay_bound", order.OrderId, order.Amount), "evt_bound");

        // The payment lands on the invoice whose order it names, and only that one.
        (await SummaryAsync(owner, paying.Id))!.Summary.Paid.Should().Be(10000m);
        (await SummaryAsync(owner, bystander.Id))!.Summary.Paid.Should().Be(0m);
    }

    // =====================================================================
    // 10. Public errors must not leak
    // =====================================================================

    [Fact]
    public async Task Public_failures_never_leak_secrets_identifiers_or_provider_detail()
    {
        var owner = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(owner);
        var token = await CreateLinkAsync(owner, invoice.Id);
        var anonymous = _factory.CreateClient();

        var order = await OrderAsync(anonymous, token);

        var bodies = new List<string>
        {
            await (await anonymous.GetAsync("/api/public/invoices/there-is-no-such-token-here-abc")).Content.ReadAsStringAsync(),
            await (await VerifyAsync(anonymous, token, order.OrderId, "pay_x", "forged")).Content.ReadAsStringAsync(),
            await (await VerifyAsync(anonymous, token, "order_not_ours", "pay_y")).Content.ReadAsStringAsync(),
            await (await WebhookAsync(anonymous, "{\"event\":\"payment.captured\"}", "evt_leak", "bad")).Content.ReadAsStringAsync(),
        };

        foreach (var body in bodies)
        {
            body.Should().NotContain(FakePaymentProvider.TestKeySecret);
            body.Should().NotContain(FakePaymentProvider.TestWebhookSecret);
            body.Should().NotContain(invoice.Id.ToString());
            body.Should().NotContain("Exception").And.NotContain("   at ");
            body.Should().NotContain("SQLite").And.NotContain("SELECT ");
        }
    }

    // =====================================================================
    // 11. Financial edge cases
    // =====================================================================

    [Fact]
    public async Task An_invoice_with_no_payments_owes_everything()
    {
        var owner = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(owner, 10000m);
        await CreateLinkAsync(owner, invoice.Id);

        var summary = (await SummaryAsync(owner, invoice.Id))!.Summary;
        summary.Total.Should().Be(10000m);
        summary.Paid.Should().Be(0m);
        summary.Outstanding.Should().Be(10000m);
        summary.OverpaidBy.Should().Be(0m);
    }

    [Theory]
    [InlineData(300000L, 3000, 7000, "PartiallyPaid")]
    [InlineData(1000000L, 10000, 0, "Paid")]
    [InlineData(1L, 0.01, 9999.99, "PartiallyPaid")]
    [InlineData(999999L, 9999.99, 0.01, "PartiallyPaid")]
    public async Task One_capture_produces_the_expected_balance_and_status(
        long capturedMinor, decimal expectedPaid, decimal expectedOutstanding, string expectedStatus)
    {
        var (owner, invoice, token, anonymous, order) = await ReadyToPayAsync(10000m);

        var paymentId = $"pay_edge_{capturedMinor}";
        _factory.Payments.Arrange(paymentId, order.OrderId, PaymentStatus.Captured, capturedMinor);
        await VerifyAsync(anonymous, token, order.OrderId, paymentId);

        var summary = (await SummaryAsync(owner, invoice.Id))!.Summary;
        summary.Paid.Should().Be(expectedPaid);
        summary.Outstanding.Should().Be(expectedOutstanding);
        summary.InvoiceStatus.Should().Be(expectedStatus);
        summary.OverpaidBy.Should().Be(0m);
    }

    [Fact]
    public async Task Two_sequential_part_payments_settle_the_invoice_exactly()
    {
        var owner = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(owner, 10000m);
        var token = await CreateLinkAsync(owner, invoice.Id);
        var anonymous = _factory.CreateClient();

        var first = await OrderAsync(anonymous, token);
        _factory.Payments.Arrange("pay_seq_1", first.OrderId, PaymentStatus.Captured, 300000);
        var afterFirst = (await (await VerifyAsync(anonymous, token, first.OrderId, "pay_seq_1"))
            .Content.ReadFromJsonAsync<VerifyPaymentResponse>())!;

        afterFirst.Paid.Should().Be(3000m);
        afterFirst.Outstanding.Should().Be(7000m);
        afterFirst.InvoiceStatus.Should().Be("PartiallyPaid");

        var second = await OrderAsync(anonymous, token);
        second.Amount.Should().Be(700000, "the second order may only collect what is left");

        _factory.Payments.Arrange("pay_seq_2", second.OrderId, PaymentStatus.Captured, 700000);
        var afterSecond = (await (await VerifyAsync(anonymous, token, second.OrderId, "pay_seq_2"))
            .Content.ReadFromJsonAsync<VerifyPaymentResponse>())!;

        afterSecond.Paid.Should().Be(10000m);
        afterSecond.Outstanding.Should().Be(0m);
        afterSecond.InvoiceStatus.Should().Be("Paid");

        var history = (await SummaryAsync(owner, invoice.Id))!;
        history.Payments.Where(p => p.Status == "Captured").Sum(p => p.Amount).Should().Be(10000m);
        history.Summary.OverpaidBy.Should().Be(0m);
    }

    [Fact]
    public async Task Rupees_and_paise_survive_the_whole_round_trip()
    {
        // A total that is awkward in binary floating point, carried from invoice to order to
        // capture to balance.
        var (owner, invoice, token, anonymous, order) = await ReadyToPayAsync(1250.50m);

        order.Amount.Should().Be(125050);

        _factory.Payments.Arrange("pay_paise", order.OrderId, PaymentStatus.Captured, 125050);
        await VerifyAsync(anonymous, token, order.OrderId, "pay_paise");

        var summary = (await SummaryAsync(owner, invoice.Id))!.Summary;
        summary.Paid.Should().Be(1250.50m);
        summary.Outstanding.Should().Be(0m);
        summary.InvoiceStatus.Should().Be("Paid");
    }
}
