using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Quotely.Api.DTOs;
using Quotely.Api.Models;
using Xunit;

namespace Quotely.Tests;

/// <summary>
/// V2.5 — overdue derived rather than stored, the receivables figures behind the dashboard, and
/// the per-customer financial summary.
///
/// The point of deriving overdue is that nothing has to run for it to become true: these tests
/// create invoices whose due dates are already in the past and expect the API to say so on the
/// very first read, with no scheduler and no status transition anywhere.
/// </summary>
public class ReceivablesApiTests : IClassFixture<QuotelyApiFactory>
{
    private readonly QuotelyApiFactory _factory;

    public ReceivablesApiTests(QuotelyApiFactory factory) => _factory = factory;

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    // ---- fixtures -------------------------------------------------------

    private static async Task<CustomerDto> CreateCustomerAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/customers", new { name, email = "c@example.com" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<CustomerDto>())!;
    }

    private static async Task<InvoiceDto> CreateInvoiceAsync(
        HttpClient client, Guid customerId, decimal amount, DateOnly dueDate,
        string status = "Sent")
    {
        // The invoice date is pulled back with the due date so an overdue invoice is not also an
        // invoice dated in the future.
        var invoiceDate = dueDate < Today ? dueDate.AddDays(-15) : Today;

        var response = await client.PostAsJsonAsync("/api/invoices", new
        {
            customerId,
            invoiceDate = invoiceDate.ToString("yyyy-MM-dd"),
            dueDate = dueDate.ToString("yyyy-MM-dd"),
            items = new object[]
            {
                new { name = "Work", unit = "Service", quantity = 1, unitPrice = amount, discount = 0, taxRate = 0 }
            }
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var invoice = (await response.Content.ReadFromJsonAsync<InvoiceDto>())!;

        if (status == "Draft") return invoice;

        var updated = await client.PutAsJsonAsync($"/api/invoices/{invoice.Id}", new
        {
            invoiceDate = invoice.InvoiceDate.ToString("yyyy-MM-dd"),
            dueDate = invoice.DueDate.ToString("yyyy-MM-dd"),
            status
        });
        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await updated.Content.ReadFromJsonAsync<InvoiceDto>())!;
    }

    private static async Task PayAsync(HttpClient client, Guid invoiceId, decimal amount)
    {
        var response = await client.PostAsJsonAsync($"/api/invoices/{invoiceId}/payments", new
        {
            amount,
            method = ManualPaymentMethods.Cash,
            paymentDate = Today.ToString("yyyy-MM-dd")
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private static Task<ReceivablesDto?> StatsAsync(HttpClient client) =>
        client.GetFromJsonAsync<ReceivablesDto>("/api/invoices/stats");

    private static Task<PagedResult<InvoiceListItemDto>?> ListAsync(HttpClient client, string query = "") =>
        client.GetFromJsonAsync<PagedResult<InvoiceListItemDto>>($"/api/invoices{query}");

    // ---- overdue derivation ---------------------------------------------

    [Fact]
    public async Task A_past_due_unpaid_invoice_reads_as_overdue()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var customer = await CreateCustomerAsync(client, "Overdue Co");

        var invoice = await CreateInvoiceAsync(client, customer.Id, 10000m, Today.AddDays(-11));

        // Nothing ran, no status changed — it is simply true now.
        (await client.GetFromJsonAsync<InvoiceDto>($"/api/invoices/{invoice.Id}"))!
            .IsOverdue.Should().BeTrue();

        var list = (await ListAsync(client))!;
        list.Items.Single(i => i.Id == invoice.Id).IsOverdue.Should().BeTrue();
    }

    [Fact]
    public async Task A_past_due_partially_paid_invoice_is_still_overdue()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var customer = await CreateCustomerAsync(client, "Part Pay Co");

        var invoice = await CreateInvoiceAsync(client, customer.Id, 10000m, Today.AddDays(-11));
        await PayAsync(client, invoice.Id, 3000m);

        var list = (await ListAsync(client))!;
        var row = list.Items.Single(i => i.Id == invoice.Id);

        row.IsOverdue.Should().BeTrue();
        row.Status.Should().Be("PartiallyPaid");
        row.Outstanding.Should().Be(7000m);
    }

    [Fact]
    public async Task Paying_a_past_due_invoice_in_full_stops_it_being_overdue()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var customer = await CreateCustomerAsync(client, "Settled Co");

        var invoice = await CreateInvoiceAsync(client, customer.Id, 10000m, Today.AddDays(-20));
        (await ListAsync(client))!.Items.Single(i => i.Id == invoice.Id).IsOverdue.Should().BeTrue();

        await PayAsync(client, invoice.Id, 10000m);

        var row = (await ListAsync(client))!.Items.Single(i => i.Id == invoice.Id);
        row.IsOverdue.Should().BeFalse("nothing is owed, however late it was");
        row.Status.Should().Be("Paid");

        (await ListAsync(client, "?status=Overdue"))!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Voiding_the_payment_makes_a_past_due_invoice_overdue_again()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var customer = await CreateCustomerAsync(client, "Void Co");

        var invoice = await CreateInvoiceAsync(client, customer.Id, 5000m, Today.AddDays(-9));
        await PayAsync(client, invoice.Id, 5000m);
        (await ListAsync(client, "?status=Overdue"))!.Items.Should().BeEmpty();

        var history = (await client.GetFromJsonAsync<InvoicePaymentsDto>($"/api/invoices/{invoice.Id}/payments"))!;
        var payment = history.Payments.Single();
        (await client.PostAsync($"/api/invoices/{invoice.Id}/payments/{payment.Id}/void", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await ListAsync(client, "?status=Overdue"))!.Items
            .Should().ContainSingle().Which.Id.Should().Be(invoice.Id);
    }

    [Fact]
    public async Task A_draft_is_never_overdue_however_old()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var customer = await CreateCustomerAsync(client, "Draft Co");

        var invoice = await CreateInvoiceAsync(client, customer.Id, 10000m, Today.AddDays(-30), status: "Draft");

        (await ListAsync(client))!.Items.Single(i => i.Id == invoice.Id)
            .IsOverdue.Should().BeFalse("a draft was never issued to anybody");
        (await ListAsync(client, "?status=Overdue"))!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task A_cancelled_invoice_is_never_overdue()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var customer = await CreateCustomerAsync(client, "Cancelled Co");

        var invoice = await CreateInvoiceAsync(client, customer.Id, 10000m, Today.AddDays(-30), status: "Cancelled");

        (await ListAsync(client))!.Items.Single(i => i.Id == invoice.Id).IsOverdue.Should().BeFalse();
        (await ListAsync(client, "?status=Overdue"))!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task An_invoice_due_today_is_not_yet_overdue()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var customer = await CreateCustomerAsync(client, "Due Today Co");

        await CreateInvoiceAsync(client, customer.Id, 10000m, Today);

        (await ListAsync(client, "?status=Overdue"))!.Items
            .Should().BeEmpty("the customer has until the end of the day");
    }

    // ---- the overdue filter ----------------------------------------------

    [Fact]
    public async Task The_overdue_filter_returns_exactly_the_overdue_invoices()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var customer = await CreateCustomerAsync(client, "Mixed Co");

        var lateUnpaid = await CreateInvoiceAsync(client, customer.Id, 10000m, Today.AddDays(-10));
        var latePartlyPaid = await CreateInvoiceAsync(client, customer.Id, 8000m, Today.AddDays(-5));
        await PayAsync(client, latePartlyPaid.Id, 2000m);

        var lateSettled = await CreateInvoiceAsync(client, customer.Id, 4000m, Today.AddDays(-3));
        await PayAsync(client, lateSettled.Id, 4000m);

        await CreateInvoiceAsync(client, customer.Id, 6000m, Today.AddDays(10));                       // not yet due
        await CreateInvoiceAsync(client, customer.Id, 1000m, Today.AddDays(-9), status: "Draft");      // never issued
        await CreateInvoiceAsync(client, customer.Id, 2000m, Today.AddDays(-9), status: "Cancelled");  // void

        var overdue = (await ListAsync(client, "?status=Overdue"))!;

        overdue.TotalCount.Should().Be(2);
        overdue.Items.Select(i => i.Id).Should().BeEquivalentTo(new[] { lateUnpaid.Id, latePartlyPaid.Id });
        overdue.Items.Should().OnlyContain(i => i.IsOverdue);
    }

    [Fact]
    public async Task The_overdue_filter_pages_in_the_database()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var customer = await CreateCustomerAsync(client, "Many Co");

        for (var i = 0; i < 5; i++)
            await CreateInvoiceAsync(client, customer.Id, 1000m, Today.AddDays(-20 + i));

        var firstPage = (await ListAsync(client, "?status=Overdue&page=1&pageSize=2"))!;

        firstPage.TotalCount.Should().Be(5, "the count is of everything matching, not of the page");
        firstPage.Items.Should().HaveCount(2);
        firstPage.TotalPages.Should().Be(3);

        var secondPage = (await ListAsync(client, "?status=Overdue&page=2&pageSize=2"))!;
        secondPage.Items.Should().HaveCount(2);
        secondPage.Items.Select(i => i.Id).Should().NotIntersectWith(firstPage.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Other_status_filters_still_match_the_stored_status()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var customer = await CreateCustomerAsync(client, "Status Co");

        var draft = await CreateInvoiceAsync(client, customer.Id, 1000m, Today.AddDays(5), status: "Draft");
        var sent = await CreateInvoiceAsync(client, customer.Id, 2000m, Today.AddDays(5));

        (await ListAsync(client, "?status=Draft"))!.Items
            .Should().ContainSingle().Which.Id.Should().Be(draft.Id);
        (await ListAsync(client, "?status=Sent"))!.Items
            .Should().ContainSingle().Which.Id.Should().Be(sent.Id);
    }

    // ---- receivables ------------------------------------------------------

    [Fact]
    public async Task Receivables_sum_what_is_owed_and_what_is_late()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var customer = await CreateCustomerAsync(client, "Books Co");

        await CreateInvoiceAsync(client, customer.Id, 10000m, Today.AddDays(-10));            // 10,000 overdue
        var partly = await CreateInvoiceAsync(client, customer.Id, 8000m, Today.AddDays(-4)); // 6,000 overdue
        await PayAsync(client, partly.Id, 2000m);
        await CreateInvoiceAsync(client, customer.Id, 5000m, Today.AddDays(20));              // 5,000 not yet due

        var stats = (await StatsAsync(client))!;

        stats.TotalOutstanding.Should().Be(21000m);
        stats.TotalOverdue.Should().Be(16000m);
        stats.CountOutstanding.Should().Be(3);
        stats.CountOverdue.Should().Be(2);
    }

    [Fact]
    public async Task Receivables_exclude_drafts_cancellations_and_settled_invoices()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var customer = await CreateCustomerAsync(client, "Excluded Co");

        await CreateInvoiceAsync(client, customer.Id, 9000m, Today.AddDays(-5), status: "Draft");
        await CreateInvoiceAsync(client, customer.Id, 7000m, Today.AddDays(-5), status: "Cancelled");
        var settled = await CreateInvoiceAsync(client, customer.Id, 4000m, Today.AddDays(-5));
        await PayAsync(client, settled.Id, 4000m);

        var stats = (await StatsAsync(client))!;

        stats.TotalOutstanding.Should().Be(0m);
        stats.TotalOverdue.Should().Be(0m);
        stats.CountOutstanding.Should().Be(0);
        stats.NeedsAttention.Should().BeEmpty();
    }

    [Fact]
    public async Task Needs_attention_lists_the_longest_overdue_first()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var customer = await CreateCustomerAsync(client, "Chase Co");

        var oldest = await CreateInvoiceAsync(client, customer.Id, 1000m, Today.AddDays(-30));
        var middle = await CreateInvoiceAsync(client, customer.Id, 2000m, Today.AddDays(-20));
        var newest = await CreateInvoiceAsync(client, customer.Id, 3000m, Today.AddDays(-10));
        await CreateInvoiceAsync(client, customer.Id, 4000m, Today.AddDays(30));

        var stats = (await StatsAsync(client))!;

        stats.NeedsAttention.Should().HaveCount(4);
        stats.NeedsAttention.Take(3).Select(i => i.Id)
            .Should().ContainInOrder(oldest.Id, middle.Id, newest.Id);
        stats.NeedsAttention[0].Outstanding.Should().Be(1000m);
        stats.NeedsAttention[0].IsOverdue.Should().BeTrue();
        stats.NeedsAttention[3].IsOverdue.Should().BeFalse("it is not due yet, only unpaid");
    }

    [Fact]
    public async Task Receivables_are_scoped_to_the_signed_in_tenant()
    {
        var owner = await _factory.CreateSignedInClientAsync();
        var customer = await CreateCustomerAsync(owner, "Mine Co");
        await CreateInvoiceAsync(owner, customer.Id, 12000m, Today.AddDays(-10));

        var other = await _factory.CreateSignedInClientAsync();
        var stats = (await StatsAsync(other))!;

        stats.TotalOutstanding.Should().Be(0m);
        stats.CountOutstanding.Should().Be(0);
        stats.NeedsAttention.Should().BeEmpty();
    }

    [Fact]
    public async Task Receivables_require_authentication()
    {
        var anonymous = _factory.CreateClient();

        (await anonymous.GetAsync("/api/invoices/stats"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---- customer summary --------------------------------------------------

    private static Task<CustomerSummaryDto?> SummaryAsync(HttpClient client, Guid customerId, string query = "") =>
        client.GetFromJsonAsync<CustomerSummaryDto>($"/api/customers/{customerId}/summary{query}");

    [Fact]
    public async Task A_customer_summary_reports_invoiced_paid_and_outstanding()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var customer = await CreateCustomerAsync(client, "Summary Co");

        var first = await CreateInvoiceAsync(client, customer.Id, 10000m, Today.AddDays(-10));
        await PayAsync(client, first.Id, 2500m);
        await CreateInvoiceAsync(client, customer.Id, 5000m, Today.AddDays(20));

        var summary = (await SummaryAsync(client, customer.Id))!;

        summary.CustomerId.Should().Be(customer.Id);
        summary.CustomerName.Should().Be("Summary Co");
        summary.TotalInvoiced.Should().Be(15000m);
        summary.TotalPaid.Should().Be(2500m);
        summary.TotalOutstanding.Should().Be(12500m);
        summary.TotalOverdue.Should().Be(7500m, "only the past-due invoice's balance is late");
        summary.OverdueCount.Should().Be(1);
        summary.InvoiceCount.Should().Be(2);
    }

    [Fact]
    public async Task A_customer_summary_counts_only_that_customers_invoices()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var mine = await CreateCustomerAsync(client, "Mine");
        var theirs = await CreateCustomerAsync(client, "Theirs");

        await CreateInvoiceAsync(client, mine.Id, 1000m, Today.AddDays(5));
        await CreateInvoiceAsync(client, theirs.Id, 9000m, Today.AddDays(5));

        var summary = (await SummaryAsync(client, mine.Id))!;

        summary.TotalInvoiced.Should().Be(1000m);
        summary.InvoiceCount.Should().Be(1);
        summary.Invoices.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task A_customer_with_more_than_one_page_of_invoices_is_fully_represented()
    {
        // The V2.4 customer page filtered page one of *all* quotations in the browser, so anything
        // past the first page vanished. The summary pages in the database instead.
        var client = await _factory.CreateSignedInClientAsync();
        var customer = await CreateCustomerAsync(client, "Busy Co");

        for (var i = 0; i < 12; i++)
            await CreateInvoiceAsync(client, customer.Id, 1000m, Today.AddDays(5));

        var firstPage = (await SummaryAsync(client, customer.Id, "?page=1&pageSize=5"))!;

        firstPage.InvoiceCount.Should().Be(12, "the totals cover every invoice, not the page");
        firstPage.TotalInvoiced.Should().Be(12000m);
        firstPage.TotalOutstanding.Should().Be(12000m);
        firstPage.Invoices.Items.Should().HaveCount(5);
        firstPage.Invoices.TotalCount.Should().Be(12);
        firstPage.Invoices.TotalPages.Should().Be(3);

        var lastPage = (await SummaryAsync(client, customer.Id, "?page=3&pageSize=5"))!;
        lastPage.Invoices.Items.Should().HaveCount(2);
        lastPage.Invoices.Items.Select(i => i.Id)
            .Should().NotIntersectWith(firstPage.Invoices.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task A_customer_summary_excludes_drafts_from_the_money_but_not_from_the_list()
    {
        var client = await _factory.CreateSignedInClientAsync();
        var customer = await CreateCustomerAsync(client, "Draft Mix Co");

        await CreateInvoiceAsync(client, customer.Id, 3000m, Today.AddDays(5));
        await CreateInvoiceAsync(client, customer.Id, 9000m, Today.AddDays(5), status: "Draft");

        var summary = (await SummaryAsync(client, customer.Id))!;

        summary.TotalInvoiced.Should().Be(3000m, "a draft is not money anybody owes");
        summary.TotalOutstanding.Should().Be(3000m);
        summary.Invoices.Items.Should().HaveCount(2, "but the owner still sees their own draft");
    }

    [Fact]
    public async Task One_tenant_cannot_read_another_tenants_customer_summary()
    {
        var owner = await _factory.CreateSignedInClientAsync();
        var customer = await CreateCustomerAsync(owner, "Private Co");
        await CreateInvoiceAsync(owner, customer.Id, 5000m, Today.AddDays(5));

        var intruder = await _factory.CreateSignedInClientAsync();

        (await intruder.GetAsync($"/api/customers/{customer.Id}/summary"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        var anonymous = _factory.CreateClient();
        (await anonymous.GetAsync($"/api/customers/{customer.Id}/summary"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_unknown_customer_summary_is_a_404()
    {
        var client = await _factory.CreateSignedInClientAsync();

        (await client.GetAsync($"/api/customers/{Guid.NewGuid()}/summary"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
