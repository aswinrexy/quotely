using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Quotely.Api.DTOs;
using Quotely.Api.Models;
using Xunit;

namespace Quotely.Tests;

/// <summary>
/// V2.5 — money that arrives outside the gateway, and the voiding of it.
///
/// The recurring assertion across this file is that manual payments are not a second system:
/// they land in the same ledger, are summed by the same calculation, and move the invoice through
/// the same statuses as a Razorpay capture does.
/// </summary>
public class ManualPaymentApiTests : IClassFixture<QuotelyApiFactory>
{
    private readonly QuotelyApiFactory _factory;

    public ManualPaymentApiTests(QuotelyApiFactory factory) => _factory = factory;

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    // ---- fixtures -------------------------------------------------------

    private static async Task<CustomerDto> CreateCustomerAsync(HttpClient client, string name = "John Smith")
    {
        var response = await client.PostAsJsonAsync("/api/customers", new
        {
            name, companyName = $"{name} Ltd", email = "john@example.com", phone = "+91 91234 56780"
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<CustomerDto>())!;
    }

    /// <summary>A directly raised invoice, issued unless the caller wants it left as a draft.</summary>
    private static async Task<InvoiceDto> CreateInvoiceAsync(
        HttpClient client, decimal amount = 10000m, bool issue = true,
        DateOnly? invoiceDate = null, DateOnly? dueDate = null)
    {
        var customer = await CreateCustomerAsync(client);
        var issued = invoiceDate ?? Today;

        var response = await client.PostAsJsonAsync("/api/invoices", new
        {
            customerId = customer.Id,
            invoiceDate = issued.ToString("yyyy-MM-dd"),
            dueDate = (dueDate ?? issued.AddDays(15)).ToString("yyyy-MM-dd"),
            items = new object[]
            {
                new { name = "Wiring", unit = "Service", quantity = 1, unitPrice = amount, discount = 0, taxRate = 0 }
            }
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var invoice = (await response.Content.ReadFromJsonAsync<InvoiceDto>())!;

        return issue ? await SetStatusAsync(client, invoice, "Sent") : invoice;
    }

    private static async Task<InvoiceDto> SetStatusAsync(HttpClient client, InvoiceDto invoice, string status)
    {
        var response = await client.PutAsJsonAsync($"/api/invoices/{invoice.Id}", new
        {
            invoiceDate = invoice.InvoiceDate.ToString("yyyy-MM-dd"),
            dueDate = invoice.DueDate.ToString("yyyy-MM-dd"),
            status
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<InvoiceDto>())!;
    }

    private static Task<HttpResponseMessage> RecordAsync(
        HttpClient client, Guid invoiceId, decimal amount,
        string method = ManualPaymentMethods.Cash, DateOnly? paymentDate = null,
        string? reference = null, string? notes = null) =>
        client.PostAsJsonAsync($"/api/invoices/{invoiceId}/payments", new
        {
            amount,
            method,
            paymentDate = (paymentDate ?? Today).ToString("yyyy-MM-dd"),
            reference,
            notes
        });

    private static async Task<PaymentDto> RecordOkAsync(
        HttpClient client, Guid invoiceId, decimal amount,
        string method = ManualPaymentMethods.Cash, DateOnly? paymentDate = null,
        string? reference = null, string? notes = null)
    {
        var response = await RecordAsync(client, invoiceId, amount, method, paymentDate, reference, notes);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<PaymentDto>())!;
    }

    private static Task<InvoiceDto?> ReadInvoiceAsync(HttpClient client, Guid id) =>
        client.GetFromJsonAsync<InvoiceDto>($"/api/invoices/{id}");

    private static Task<InvoicePaymentsDto?> ReadPaymentsAsync(HttpClient client, Guid id) =>
        client.GetFromJsonAsync<InvoicePaymentsDto>($"/api/invoices/{id}/payments");

    // ---- recording ------------------------------------------------------

    [Theory]
    [InlineData(ManualPaymentMethods.Cash)]
    [InlineData(ManualPaymentMethods.BankTransfer)]
    [InlineData(ManualPaymentMethods.Upi)]
    [InlineData(ManualPaymentMethods.Cheque)]
    [InlineData(ManualPaymentMethods.Other)]
    public async Task An_owner_can_record_money_received_by_any_supported_method(string method)
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client);

        var payment = await RecordOkAsync(client, invoice.Id, 10000m, method);

        payment.Amount.Should().Be(10000m);
        payment.Method.Should().Be(method);
        payment.Source.Should().Be(nameof(PaymentSource.Manual));
        payment.Provider.Should().Be(PaymentProviders.Manual);
        payment.Status.Should().Be(nameof(PaymentStatus.Captured), "the owner already holds this money");
        payment.CanVoid.Should().BeTrue();

        (await ReadInvoiceAsync(client, invoice.Id))!.Status.Should().Be("Paid");
    }

    [Fact]
    public async Task A_manual_payment_carries_no_provider_order_or_payment_id()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client);

        await RecordOkAsync(client, invoice.Id, 5000m, ManualPaymentMethods.Cheque, reference: "CHQ-88123");

        // Straight from the database: no placeholder, no empty string, genuinely null.
        var rows = await _factory.ReadPaymentsAsync(invoice.Id);
        var manual = rows.Should().ContainSingle().Which;

        manual.ProviderOrderId.Should().BeNull();
        manual.ProviderPaymentId.Should().BeNull();
        manual.ReservationSlot.Should().BeNull("a manual payment has no checkout window to hold");
        manual.Source.Should().Be(PaymentSource.Manual);
        manual.Reference.Should().Be("CHQ-88123");
    }

    [Fact]
    public async Task Reference_and_notes_are_stored_and_returned()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client);

        var payment = await RecordOkAsync(
            client, invoice.Id, 2500m, ManualPaymentMethods.BankTransfer,
            reference: "UTR9988776655", notes: "Paid at the site office");

        payment.Reference.Should().Be("UTR9988776655");
        payment.Notes.Should().Be("Paid at the site office");
    }

    [Fact]
    public async Task A_payment_date_may_be_back_dated()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client);

        var lastWeek = Today.AddDays(-7);
        var payment = await RecordOkAsync(client, invoice.Id, 1000m, paymentDate: lastWeek);

        payment.PaidAt!.Value.Date.Should().Be(lastWeek.ToDateTime(TimeOnly.MinValue));
    }

    // ---- partial and mixed ----------------------------------------------

    [Fact]
    public async Task A_partial_manual_payment_leaves_the_balance_outstanding()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client);

        await RecordOkAsync(client, invoice.Id, 3000m);

        var reread = (await ReadInvoiceAsync(client, invoice.Id))!;
        reread.Paid.Should().Be(3000m);
        reread.Outstanding.Should().Be(7000m);
        reread.Status.Should().Be("PartiallyPaid");
    }

    [Fact]
    public async Task Paying_the_remainder_closes_the_invoice()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client);

        await RecordOkAsync(client, invoice.Id, 3000m);
        await RecordOkAsync(client, invoice.Id, 7000m, ManualPaymentMethods.BankTransfer);

        var reread = (await ReadInvoiceAsync(client, invoice.Id))!;
        reread.Paid.Should().Be(10000m);
        reread.Outstanding.Should().Be(0m);
        reread.Status.Should().Be("Paid");
    }

    [Fact]
    public async Task Manual_and_gateway_payments_settle_one_invoice_together()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client);

        // ₹3,000 in cash…
        await RecordOkAsync(client, invoice.Id, 3000m);

        // …then ₹4,000 through Razorpay, which opens an order for whatever is outstanding.
        var token = await CreateLinkAsync(client, invoice.Id);
        var anonymous = _factory.CreateClient();
        var order = (await (await anonymous.PostAsync(
                $"/api/public/invoices/{token}/create-payment-order", null))
            .Content.ReadFromJsonAsync<PaymentOrderDto>())!;

        order.Amount.Should().Be(700_000, "the gateway is asked for the ₹7,000 still owed, not the ₹10,000 total");

        _factory.Payments.Arrange("pay_mixed_1", order.OrderId, PaymentStatus.Captured, 400_000);
        (await anonymous.PostAsJsonAsync($"/api/public/invoices/{token}/verify-payment", new
        {
            razorpayPaymentId = "pay_mixed_1",
            razorpayOrderId = order.OrderId,
            razorpaySignature = FakePaymentProvider.CheckoutSignature(order.OrderId, "pay_mixed_1")
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        // …and the last ₹3,000 by bank transfer.
        await RecordOkAsync(client, invoice.Id, 3000m, ManualPaymentMethods.BankTransfer);

        var reread = (await ReadInvoiceAsync(client, invoice.Id))!;
        reread.Paid.Should().Be(10000m);
        reread.Outstanding.Should().Be(0m);
        reread.Status.Should().Be("Paid");

        var history = (await ReadPaymentsAsync(client, invoice.Id))!;
        history.Payments.Count(p => p.Source == nameof(PaymentSource.Manual)).Should().Be(2);
        history.Payments.Count(p => p.Source == nameof(PaymentSource.Gateway)).Should().Be(1);
    }

    private static async Task<string> CreateLinkAsync(HttpClient client, Guid invoiceId)
    {
        var response = await client.PostAsync($"/api/invoices/{invoiceId}/public-link", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var link = (await response.Content.ReadFromJsonAsync<PublicInvoiceLinkDto>())!;
        return link.Url[(link.Url.LastIndexOf('/') + 1)..];
    }

    // ---- validation ------------------------------------------------------

    [Fact]
    public async Task More_than_the_outstanding_balance_is_refused()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client);

        (await RecordAsync(client, invoice.Id, 10001m))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await RecordOkAsync(client, invoice.Id, 6000m);

        // The ceiling moves down with the balance.
        (await RecordAsync(client, invoice.Id, 4001m))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await ReadInvoiceAsync(client, invoice.Id))!.Paid.Should().Be(6000m, "nothing was over-collected");
    }

    [Fact]
    public async Task A_fully_paid_invoice_takes_no_further_payment()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client);

        await RecordOkAsync(client, invoice.Id, 10000m);

        (await RecordAsync(client, invoice.Id, 1m))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public async Task A_non_positive_amount_is_refused(decimal amount)
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client);

        (await RecordAsync(client, invoice.Id, amount))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_genuinely_future_payment_date_is_refused()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client);

        (await RecordAsync(client, invoice.Id, 1000m, paymentDate: Today.AddDays(2)))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// The owner's "today" is a local date, and east of UTC it runs ahead of the server's. A
    /// business in Chennai entering a cash payment at 1am is a full calendar day ahead of UTC,
    /// and refusing that made the app look broken every night between midnight and 05:30. One
    /// day of tolerance covers every timezone, since none is further ahead than UTC+14.
    /// </summary>
    [Fact]
    public async Task A_payment_dated_tomorrow_in_utc_is_accepted_for_owners_east_of_it()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client);

        var response = await RecordAsync(client, invoice.Id, 1000m, paymentDate: Today.AddDays(1));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await ReadInvoiceAsync(client, invoice.Id))!.Paid.Should().Be(1000m);
    }

    [Theory]
    [InlineData("bitcoin")]
    [InlineData("")]
    [InlineData("card")]  // a gateway method, not something an owner records by hand
    public async Task An_unsupported_method_is_refused(string method)
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client);

        (await RecordAsync(client, invoice.Id, 1000m, method))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_draft_invoice_takes_no_payment()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client, issue: false);
        invoice.Status.Should().Be("Draft");

        (await RecordAsync(client, invoice.Id, 1000m))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_cancelled_invoice_takes_no_payment()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client);
        await SetStatusAsync(client, invoice, "Cancelled");

        (await RecordAsync(client, invoice.Id, 1000m))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task An_unknown_or_other_tenants_invoice_is_a_404()
    {
        var owner = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(owner);

        var intruder = await _factory.CreateSignedInClientAsync();

        (await RecordAsync(intruder, invoice.Id, 1000m))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await RecordAsync(owner, Guid.NewGuid(), 1000m))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await ReadInvoiceAsync(owner, invoice.Id))!.Paid.Should().Be(0m);
    }

    [Fact]
    public async Task An_unauthenticated_request_cannot_record_a_payment()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client);

        var anonymous = _factory.CreateClient();

        (await RecordAsync(anonymous, invoice.Id, 1000m))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---- voiding ---------------------------------------------------------

    private static Task<HttpResponseMessage> VoidAsync(HttpClient client, Guid invoiceId, Guid paymentId) =>
        client.PostAsync($"/api/invoices/{invoiceId}/payments/{paymentId}/void", null);

    [Fact]
    public async Task Voiding_a_manual_payment_returns_the_whole_balance_to_outstanding()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client);

        var payment = await RecordOkAsync(client, invoice.Id, 10000m);
        (await ReadInvoiceAsync(client, invoice.Id))!.Status.Should().Be("Paid");

        var response = await VoidAsync(client, invoice.Id, payment.Id);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var voided = (await response.Content.ReadFromJsonAsync<PaymentDto>())!;
        voided.IsVoided.Should().BeTrue();
        voided.VoidedAt.Should().NotBeNull();
        voided.CanVoid.Should().BeFalse();

        var reread = (await ReadInvoiceAsync(client, invoice.Id))!;
        reread.Paid.Should().Be(0m);
        reread.Outstanding.Should().Be(10000m);
        reread.Status.Should().NotBe("Paid");
        reread.Status.Should().Be("Sent", "the invoice is issued and awaiting payment again");
    }

    [Fact]
    public async Task Voiding_one_of_two_payments_moves_paid_back_to_partially_paid()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client);

        var first = await RecordOkAsync(client, invoice.Id, 3000m);
        await RecordOkAsync(client, invoice.Id, 7000m, ManualPaymentMethods.Upi);
        (await ReadInvoiceAsync(client, invoice.Id))!.Status.Should().Be("Paid");

        (await VoidAsync(client, invoice.Id, first.Id)).StatusCode.Should().Be(HttpStatusCode.OK);

        var reread = (await ReadInvoiceAsync(client, invoice.Id))!;
        reread.Paid.Should().Be(7000m);
        reread.Outstanding.Should().Be(3000m);
        reread.Status.Should().Be("PartiallyPaid");
    }

    [Fact]
    public async Task A_voided_payment_stays_on_the_record()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client);

        var payment = await RecordOkAsync(client, invoice.Id, 4000m, notes: "Recorded twice by mistake");
        await VoidAsync(client, invoice.Id, payment.Id);

        var history = (await ReadPaymentsAsync(client, invoice.Id))!;
        var row = history.Payments.Should().ContainSingle(p => p.Id == payment.Id).Which;

        row.IsVoided.Should().BeTrue();
        row.Amount.Should().Be(4000m, "the record keeps what was entered");
        row.Notes.Should().Be("Recorded twice by mistake");
        history.Summary.Paid.Should().Be(0m, "but it no longer counts");

        // And it is still physically present, not deleted.
        (await _factory.ReadPaymentsAsync(invoice.Id)).Should().ContainSingle()
            .Which.VoidedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task A_payment_cannot_be_voided_twice()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client);

        var payment = await RecordOkAsync(client, invoice.Id, 1000m);
        (await VoidAsync(client, invoice.Id, payment.Id)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await VoidAsync(client, invoice.Id, payment.Id)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await ReadInvoiceAsync(client, invoice.Id))!.Outstanding.Should().Be(10000m);
    }

    [Fact]
    public async Task A_gateway_payment_cannot_be_voided()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client);

        var token = await CreateLinkAsync(client, invoice.Id);
        var anonymous = _factory.CreateClient();
        var order = (await (await anonymous.PostAsync(
                $"/api/public/invoices/{token}/create-payment-order", null))
            .Content.ReadFromJsonAsync<PaymentOrderDto>())!;

        _factory.Payments.Arrange("pay_novoid", order.OrderId, PaymentStatus.Captured, order.Amount);
        await anonymous.PostAsJsonAsync($"/api/public/invoices/{token}/verify-payment", new
        {
            razorpayPaymentId = "pay_novoid",
            razorpayOrderId = order.OrderId,
            razorpaySignature = FakePaymentProvider.CheckoutSignature(order.OrderId, "pay_novoid")
        });

        var history = (await ReadPaymentsAsync(client, invoice.Id))!;
        var gateway = history.Payments.Should().ContainSingle(p => p.Source == nameof(PaymentSource.Gateway)).Which;
        gateway.CanVoid.Should().BeFalse("the UI must not offer a void action on gateway money");

        (await VoidAsync(client, invoice.Id, gateway.Id)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await ReadInvoiceAsync(client, invoice.Id))!.Paid.Should().Be(10000m, "the capture still stands");
    }

    [Fact]
    public async Task One_tenant_cannot_void_another_tenants_payment()
    {
        var owner = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(owner);
        var payment = await RecordOkAsync(owner, invoice.Id, 5000m);

        var intruder = await _factory.CreateSignedInClientAsync();

        (await VoidAsync(intruder, invoice.Id, payment.Id))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await ReadInvoiceAsync(owner, invoice.Id))!.Paid.Should().Be(5000m);
    }

    [Fact]
    public async Task A_payment_cannot_be_voided_through_a_different_invoice()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var first = await CreateInvoiceAsync(client);
        var second = await CreateInvoiceAsync(client);

        var payment = await RecordOkAsync(client, first.Id, 5000m);

        // The caller owns both, but the payment does not belong to the invoice in the route.
        (await VoidAsync(client, second.Id, payment.Id))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await ReadInvoiceAsync(client, first.Id))!.Paid.Should().Be(5000m);
    }

    [Fact]
    public async Task Recording_a_payment_again_after_voiding_works_normally()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client);

        // The classic correction: wrong amount entered, voided, re-entered correctly.
        var wrong = await RecordOkAsync(client, invoice.Id, 9000m);
        await VoidAsync(client, invoice.Id, wrong.Id);
        await RecordOkAsync(client, invoice.Id, 4500m);

        var reread = (await ReadInvoiceAsync(client, invoice.Id))!;
        reread.Paid.Should().Be(4500m);
        reread.Outstanding.Should().Be(5500m);
        reread.Status.Should().Be("PartiallyPaid");
    }


    [Fact]
    public async Task A_voided_payment_disappears_from_the_customers_public_invoice_page()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client);

        var standing = await RecordOkAsync(client, invoice.Id, 3000m);
        var mistake = await RecordOkAsync(client, invoice.Id, 4000m);
        await VoidAsync(client, invoice.Id, mistake.Id);

        var link = (await (await client.PostAsync($"/api/invoices/{invoice.Id}/public-link", null))
            .Content.ReadFromJsonAsync<PublicInvoiceLinkDto>())!;
        var token = link.Url[(link.Url.LastIndexOf('/') + 1)..];

        var page = (await _factory.CreateClient()
            .GetFromJsonAsync<PublicInvoiceDto>($"/api/public/invoices/{token}"))!;

        // The customer is asked for 7,000 — so the payments listed beneath must add up to 3,000,
        // not to the 7,000 that includes money the owner has since struck out.
        page.Paid.Should().Be(3000m);
        page.Outstanding.Should().Be(7000m);
        page.Payments.Should().ContainSingle().Which.Amount.Should().Be(standing.Amount);
    }

    // ---- history ----------------------------------------------------------

    [Fact]
    public async Task The_history_shows_both_sources_in_one_list()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var invoice = await CreateInvoiceAsync(client);

        await RecordOkAsync(client, invoice.Id, 2000m, ManualPaymentMethods.Cash);

        var token = await CreateLinkAsync(client, invoice.Id);
        var anonymous = _factory.CreateClient();
        var order = (await (await anonymous.PostAsync(
                $"/api/public/invoices/{token}/create-payment-order", null))
            .Content.ReadFromJsonAsync<PaymentOrderDto>())!;
        _factory.Payments.Arrange("pay_hist", order.OrderId, PaymentStatus.Captured, 500_000, method: "netbanking");
        await anonymous.PostAsJsonAsync($"/api/public/invoices/{token}/verify-payment", new
        {
            razorpayPaymentId = "pay_hist",
            razorpayOrderId = order.OrderId,
            razorpaySignature = FakePaymentProvider.CheckoutSignature(order.OrderId, "pay_hist")
        });

        var history = (await ReadPaymentsAsync(client, invoice.Id))!;

        history.Payments.Should().HaveCount(2);
        history.Summary.Paid.Should().Be(7000m);
        history.Summary.Outstanding.Should().Be(3000m);

        var manual = history.Payments.Single(p => p.Source == nameof(PaymentSource.Manual));
        manual.Method.Should().Be("cash");
        manual.OrderReference.Should().BeNull("there is no gateway order behind cash");

        var gateway = history.Payments.Single(p => p.Source == nameof(PaymentSource.Gateway));
        gateway.Method.Should().Be("netbanking");
        gateway.Reference.Should().Be("pay_hist");
    }
}
