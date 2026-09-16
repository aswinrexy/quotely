using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Quotely.Api.DTOs;
using Quotely.Api.Services;
using Xunit;

namespace Quotely.Tests;

/// <summary>Covers the V2.1 customer-facing share link: creation, access, response and abuse paths.</summary>
public class PublicQuotationApiTests : IClassFixture<QuotelyApiFactory>
{
    private readonly QuotelyApiFactory _factory;

    public PublicQuotationApiTests(QuotelyApiFactory factory) => _factory = factory;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private static object Payload(Guid customerId, DateOnly? validUntil = null, string? status = null) => new
    {
        customerId,
        quotationDate = Today.AddDays(-1).ToString("yyyy-MM-dd"),
        validUntil = (validUntil ?? Today.AddDays(15)).ToString("yyyy-MM-dd"),
        status,
        notes = "Thank you for your business.",
        terms = "Valid until the stated date.",
        items = new object[]
        {
            new { name = "AC Installation", unit = "Service", quantity = 2, unitPrice = 5000, discount = 500, taxRate = 18 },
            new { name = "AC Maintenance", unit = "Service", quantity = 1, unitPrice = 1500, discount = 0, taxRate = 18 }
        }
    };

    /// <summary>Creates an owner with a quotation and returns the owner client plus the quotation.</summary>
    private async Task<(HttpClient Owner, QuotationDto Quotation)> NewQuotationAsync(
        DateOnly? validUntil = null, string? status = null)
    {
        var owner = await _factory.CreateSignedInClientAsync();
        var customer = await (await owner.PostAsJsonAsync("/api/customers", new
        {
            name = "John Smith",
            companyName = "John Smith Construction",
            email = "john@example.com",
            phone = "+91 91234 56780",
            addressLine = "42 Beach Road",
            city = "Chennai"
        })).Content.ReadFromJsonAsync<CustomerDto>();

        var quotation = await (await owner.PostAsJsonAsync("/api/quotations", Payload(customer!.Id, validUntil, status)))
            .Content.ReadFromJsonAsync<QuotationDto>();

        return (owner, quotation!);
    }

    private static async Task<PublicQuotationLinkDto> CreateLinkDtoAsync(HttpClient owner, Guid quotationId)
    {
        var response = await owner.PostAsync($"/api/quotations/{quotationId}/public-link", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<PublicQuotationLinkDto>())!;
    }

    private static async Task<string> CreateLinkAsync(HttpClient owner, Guid quotationId)
        => (await CreateLinkDtoAsync(owner, quotationId)).Url;

    private static string TokenFrom(string url) => url[(url.LastIndexOf('/') + 1)..];

    private static HttpClient Anonymous(QuotelyApiFactory factory) => factory.CreateClient();

    // ---- link creation -------------------------------------------------

    [Fact]
    public async Task Owner_can_create_a_share_link_for_their_own_quotation()
    {
        var (owner, quotation) = await NewQuotationAsync();

        var url = await CreateLinkAsync(owner, quotation.Id);

        url.Should().StartWith("http://localhost:3000/q/");
        TokenFrom(url).Length.Should().BeGreaterThanOrEqualTo(43, "the token carries 256 bits of entropy");
    }

    [Fact]
    public async Task The_public_url_contains_no_internal_identifiers()
    {
        var (owner, quotation) = await NewQuotationAsync();

        var url = await CreateLinkAsync(owner, quotation.Id);

        url.Should().NotContain(quotation.Id.ToString());
        url.Should().NotContain(quotation.Customer.Id.ToString());
        url.Should().NotContain(quotation.QuotationNumber);
    }

    [Fact]
    public async Task The_raw_token_is_never_stored__only_its_hash()
    {
        var (owner, quotation) = await NewQuotationAsync();
        var token = TokenFrom(await CreateLinkAsync(owner, quotation.Id));

        var stored = await _factory.ReadQuotationAsync(quotation.Id, q => new
        {
            q.PublicTokenHash,
            q.PublicLinkCreatedAt
        });

        stored.PublicTokenHash.Should().NotBeNullOrEmpty();
        stored.PublicTokenHash.Should().NotBe(token, "the raw token must never be persisted");
        stored.PublicTokenHash.Should().Be(PublicTokenGenerator.Hash(token));
        stored.PublicLinkCreatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Creating_a_link_moves_a_draft_to_sent_and_leaves_other_statuses_alone()
    {
        var (owner, draft) = await NewQuotationAsync();
        draft.Status.Should().Be("Draft");

        await CreateLinkAsync(owner, draft.Id);

        var afterShare = await owner.GetFromJsonAsync<QuotationDto>($"/api/quotations/{draft.Id}");
        afterShare!.Status.Should().Be("Sent");
        afterShare.HasPublicLink.Should().BeTrue();

        var (secondOwner, alreadySent) = await NewQuotationAsync(status: "Sent");
        await CreateLinkAsync(secondOwner, alreadySent.Id);
        (await secondOwner.GetFromJsonAsync<QuotationDto>($"/api/quotations/{alreadySent.Id}"))!
            .Status.Should().Be("Sent");
    }

    [Fact]
    public async Task A_user_cannot_create_a_share_link_for_another_users_quotation()
    {
        var (_, quotation) = await NewQuotationAsync();
        var intruder = await _factory.CreateSignedInClientAsync();

        var response = await intruder.PostAsync($"/api/quotations/{quotation.Id}/public-link", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Creating_a_share_link_requires_authentication()
    {
        var (_, quotation) = await NewQuotationAsync();

        var response = await Anonymous(_factory).PostAsync($"/api/quotations/{quotation.Id}/public-link", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Regenerating_a_link_retires_the_previous_one()
    {
        var (owner, quotation) = await NewQuotationAsync();
        var first = TokenFrom(await CreateLinkAsync(owner, quotation.Id));
        var second = TokenFrom(await CreateLinkAsync(owner, quotation.Id));

        second.Should().NotBe(first);

        var client = Anonymous(_factory);
        (await client.GetAsync($"/api/public/quotations/{first}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/public/quotations/{second}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---- public read ---------------------------------------------------

    [Fact]
    public async Task The_token_returns_the_right_quotation_without_a_jwt()
    {
        var (owner, quotation) = await NewQuotationAsync();
        var token = TokenFrom(await CreateLinkAsync(owner, quotation.Id));

        var dto = await Anonymous(_factory).GetFromJsonAsync<PublicQuotationDto>($"/api/public/quotations/{token}");

        dto!.QuotationNumber.Should().Be(quotation.QuotationNumber);
        dto.Customer.Name.Should().Be("John Smith");
        dto.Items.Should().HaveCount(2);
        dto.GrandTotal.Should().Be(12980m);
        dto.Subtotal.Should().Be(11500m);
        dto.TaxTotal.Should().Be(1980m);
        dto.CanRespond.Should().BeTrue();
        dto.IsExpired.Should().BeFalse();
    }

    [Fact]
    public async Task The_public_payload_leaks_no_internal_state()
    {
        var (owner, quotation) = await NewQuotationAsync();
        var token = TokenFrom(await CreateLinkAsync(owner, quotation.Id));

        var body = await Anonymous(_factory).GetStringAsync($"/api/public/quotations/{token}");

        body.Should().NotContain(quotation.Id.ToString());
        body.Should().NotContain(quotation.Customer.Id.ToString());
        body.Should().NotContain(PublicTokenGenerator.Hash(token));
        body.Should().NotContain(token);

        using var json = JsonDocument.Parse(body);
        var names = json.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        names.Should().NotContain("id");
        names.Should().NotContain("userId");
        names.Should().NotContain("customerId");
        names.Should().NotContain("publicTokenHash");
        json.RootElement.GetProperty("customer").EnumerateObject()
            .Select(p => p.Name).Should().NotContain("id");
    }

    [Theory]
    [InlineData("does-not-exist-but-is-well-formed-token")]
    [InlineData("../../api/quotations")]
    [InlineData("' OR 1=1--")]
    public async Task An_unknown_or_malformed_token_is_a_plain_404(string token)
    {
        var response = await Anonymous(_factory).GetAsync($"/api/public/quotations/{Uri.EscapeDataString(token)}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("Quotations", "no schema detail may leak");
    }

    [Fact]
    public async Task A_quotation_without_a_link_is_unreachable_publicly()
    {
        var (owner, quotation) = await NewQuotationAsync();

        // The internal id is not a credential: it must not open the public endpoint.
        var response = await Anonymous(_factory).GetAsync($"/api/public/quotations/{quotation.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.GetFromJsonAsync<QuotationDto>($"/api/quotations/{quotation.Id}"))!
            .HasPublicLink.Should().BeFalse();
    }

    [Fact]
    public async Task The_authenticated_quotation_api_stays_behind_jwt()
    {
        var (_, quotation) = await NewQuotationAsync();
        var client = Anonymous(_factory);

        foreach (var path in new[] { "/api/quotations", $"/api/quotations/{quotation.Id}", $"/api/quotations/{quotation.Id}/pdf" })
            (await client.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"{path} must stay protected");
    }

    // ---- accept --------------------------------------------------------

    [Fact]
    public async Task A_customer_can_accept_and_the_response_is_recorded()
    {
        var (owner, quotation) = await NewQuotationAsync();
        var token = TokenFrom(await CreateLinkAsync(owner, quotation.Id));
        var before = DateTime.UtcNow.AddSeconds(-1);

        var response = await Anonymous(_factory).PostAsJsonAsync($"/api/public/quotations/{token}/accept", new
        {
            name = "John Smith",
            email = "john@example.com",
            comment = "Approved. Please proceed."
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<PublicQuotationDto>();
        dto!.Status.Should().Be("Accepted");
        dto.CanRespond.Should().BeFalse();
        dto.RespondedAt.Should().NotBeNull().And.BeAfter(before);

        // The owner sees the same result through the authenticated API.
        var owned = await owner.GetFromJsonAsync<QuotationDto>($"/api/quotations/{quotation.Id}");
        owned!.Status.Should().Be("Accepted");
        owned.RespondedByName.Should().Be("John Smith");
        owned.RespondedByEmail.Should().Be("john@example.com");
        owned.ResponseComment.Should().Be("Approved. Please proceed.");
        owned.RespondedAt.Should().NotBeNull();
        owned.Customer.Name.Should().Be("John Smith", "the customer record itself is untouched");
    }

    [Fact]
    public async Task Accepting_twice_returns_a_conflict_and_keeps_the_first_answer()
    {
        var (owner, quotation) = await NewQuotationAsync();
        var token = TokenFrom(await CreateLinkAsync(owner, quotation.Id));
        var client = Anonymous(_factory);

        await client.PostAsJsonAsync($"/api/public/quotations/{token}/accept", new { name = "John Smith" });

        var again = await client.PostAsJsonAsync($"/api/public/quotations/{token}/accept", new { name = "Someone Else" });
        var flip = await client.PostAsJsonAsync($"/api/public/quotations/{token}/reject", new { name = "Someone Else" });

        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        flip.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var owned = await owner.GetFromJsonAsync<QuotationDto>($"/api/quotations/{quotation.Id}");
        owned!.Status.Should().Be("Accepted");
        owned.RespondedByName.Should().Be("John Smith");
    }

    [Fact]
    public async Task An_expired_quotation_can_be_viewed_but_not_answered()
    {
        var (owner, quotation) = await NewQuotationAsync(validUntil: Today.AddDays(-1));
        var token = TokenFrom(await CreateLinkAsync(owner, quotation.Id));
        var client = Anonymous(_factory);

        var view = await client.GetFromJsonAsync<PublicQuotationDto>($"/api/public/quotations/{token}");
        view!.IsExpired.Should().BeTrue();
        view.CanRespond.Should().BeFalse();

        var accept = await client.PostAsJsonAsync($"/api/public/quotations/{token}/accept", new { name = "John Smith" });
        var reject = await client.PostAsJsonAsync($"/api/public/quotations/{token}/reject", new { name = "John Smith" });

        accept.StatusCode.Should().Be(HttpStatusCode.Conflict);
        reject.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await owner.GetFromJsonAsync<QuotationDto>($"/api/quotations/{quotation.Id}"))!
            .Status.Should().Be("Sent", "an expired quotation keeps its status");
    }

    [Fact]
    public async Task Response_timestamps_come_back_as_unambiguous_utc()
    {
        var (owner, quotation) = await NewQuotationAsync();
        var token = TokenFrom(await CreateLinkAsync(owner, quotation.Id));
        await Anonymous(_factory).PostAsJsonAsync($"/api/public/quotations/{token}/accept", new { name = "John Smith" });

        // Read back through the API: a timestamp without a "Z" is parsed as local time by browsers,
        // which shifts the date the customer and owner see.
        var body = await Anonymous(_factory).GetStringAsync($"/api/public/quotations/{token}");
        var respondedAt = JsonDocument.Parse(body).RootElement.GetProperty("respondedAt").GetString();

        respondedAt.Should().NotBeNull().And.EndWith("Z");
        DateTime.Parse(respondedAt!, null, System.Globalization.DateTimeStyles.RoundtripKind)
            .Kind.Should().Be(DateTimeKind.Utc);
    }

    // ---- reject --------------------------------------------------------

    [Fact]
    public async Task A_customer_can_reject_with_a_reason()
    {
        var (owner, quotation) = await NewQuotationAsync();
        var token = TokenFrom(await CreateLinkAsync(owner, quotation.Id));

        var response = await Anonymous(_factory).PostAsJsonAsync($"/api/public/quotations/{token}/reject", new
        {
            name = "John Smith",
            comment = "The price is outside our budget."
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<PublicQuotationDto>())!.Status.Should().Be("Rejected");

        var owned = await owner.GetFromJsonAsync<QuotationDto>($"/api/quotations/{quotation.Id}");
        owned!.Status.Should().Be("Rejected");
        owned.ResponseComment.Should().Be("The price is outside our budget.");
        owned.RespondedByEmail.Should().BeNull("email is optional");
    }

    [Fact]
    public async Task Rejecting_twice_returns_a_conflict()
    {
        var (owner, quotation) = await NewQuotationAsync();
        var token = TokenFrom(await CreateLinkAsync(owner, quotation.Id));
        var client = Anonymous(_factory);

        await client.PostAsJsonAsync($"/api/public/quotations/{token}/reject", new { name = "John Smith" });
        var again = await client.PostAsJsonAsync($"/api/public/quotations/{token}/reject", new { name = "John Smith" });

        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ---- validation ----------------------------------------------------

    [Theory]
    [InlineData("", null, null, "name is required")]
    [InlineData("   ", null, null, "blank name is rejected")]
    [InlineData("John Smith", "not-an-email", null, "email must be well formed")]
    public async Task Invalid_responses_are_rejected(string name, string? email, string? comment, string because)
    {
        var (owner, quotation) = await NewQuotationAsync();
        var token = TokenFrom(await CreateLinkAsync(owner, quotation.Id));

        var response = await Anonymous(_factory)
            .PostAsJsonAsync($"/api/public/quotations/{token}/accept", new { name, email, comment });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, because);
        (await owner.GetFromJsonAsync<QuotationDto>($"/api/quotations/{quotation.Id}"))!
            .Status.Should().Be("Sent", "a rejected payload must not change anything");
    }

    [Fact]
    public async Task An_oversized_comment_is_rejected()
    {
        var (owner, quotation) = await NewQuotationAsync();
        var token = TokenFrom(await CreateLinkAsync(owner, quotation.Id));

        var response = await Anonymous(_factory).PostAsJsonAsync($"/api/public/quotations/{token}/accept", new
        {
            name = "John Smith",
            comment = new string('x', 5000)
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_customer_cannot_set_the_status_or_the_totals()
    {
        var (owner, quotation) = await NewQuotationAsync();
        var token = TokenFrom(await CreateLinkAsync(owner, quotation.Id));

        // Extra fields are simply not bound: the decision comes from the route, amounts from the database.
        await Anonymous(_factory).PostAsJsonAsync($"/api/public/quotations/{token}/accept", new
        {
            name = "John Smith",
            status = "Rejected",
            grandTotal = 1m,
            subtotal = 1m,
            items = new object[] { new { name = "Injected", quantity = 99, unitPrice = 0 } }
        });

        var owned = await owner.GetFromJsonAsync<QuotationDto>($"/api/quotations/{quotation.Id}");
        owned!.Status.Should().Be("Accepted", "the route decides the outcome, not the payload");
        owned.GrandTotal.Should().Be(12980m, "totals stay server-owned");
        owned.Items.Should().HaveCount(2, "a customer cannot change line items");
    }

    // ---- public pdf ----------------------------------------------------

    [Fact]
    public async Task The_share_token_also_serves_the_pdf()
    {
        var (owner, quotation) = await NewQuotationAsync();
        var token = TokenFrom(await CreateLinkAsync(owner, quotation.Id));

        var response = await Anonymous(_factory).GetAsync($"/api/public/quotations/{token}/pdf");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public async Task An_unknown_token_cannot_fetch_a_pdf()
    {
        var response = await Anonymous(_factory).GetAsync("/api/public/quotations/unknown-token-value-1234567890/pdf");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- V2.6: share material -------------------------------------------

    [Fact]
    public async Task Creating_a_link_returns_ready_to_send_share_material()
    {
        var (owner, quotation) = await NewQuotationAsync();

        var link = await CreateLinkDtoAsync(owner, quotation.Id);
        var share = link.Share;

        share.Url.Should().Be(link.Url, "the message and the copy action must point at one link");
        share.Message.Should().Be(
            "Hi John,\n\n" +
            "Please find quotation " + quotation.QuotationNumber + " from Test Business.\n\n" +
            "Total: \u20b912,980.00\n" +
            "Valid until: " + quotation.ValidUntil.ToString("dd MMM yyyy") + "\n\n" +
            "View quotation:\n" + link.Url + "\n\n" +
            "Thank you.");
        share.EmailSubject.Should().Be($"Quotation {quotation.QuotationNumber} from Test Business");

        // Addressed from the stored customer record, normalised for each transport.
        share.CustomerPhone.Should().Be("919123456780");
        share.CustomerEmail.Should().Be("john@example.com");
        share.WhatsAppUrl.Should().StartWith("https://wa.me/919123456780?text=");
        share.MailtoUrl.Should().StartWith("mailto:john%40example.com?subject=");
        Uri.UnescapeDataString(share.WhatsAppUrl["https://wa.me/919123456780?text=".Length..])
            .Should().Be(share.Message);
    }

    [Fact]
    public async Task The_share_figures_come_from_the_server_and_ignore_anything_posted()
    {
        var (owner, quotation) = await NewQuotationAsync();

        // A caller trying to dictate the number, the total or the business name gets none of it:
        // the endpoint reads only the route id and the authenticated user.
        var response = await owner.PostAsJsonAsync($"/api/quotations/{quotation.Id}/public-link", new
        {
            quotationNumber = "QT-999999",
            grandTotal = 1m,
            businessName = "Somebody Else",
            url = "https://evil.example/q/attacker"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var share = (await response.Content.ReadFromJsonAsync<PublicQuotationLinkDto>())!.Share;

        share.Message.Should().Contain(quotation.QuotationNumber).And.Contain("Test Business");
        share.Message.Should().Contain("\u20b912,980.00");
        share.Message.Should().NotContain("QT-999999")
            .And.NotContain("Somebody Else")
            .And.NotContain("evil.example");
        share.Url.Should().StartWith("http://localhost:3000/q/");
    }

    [Fact]
    public async Task The_share_material_carries_no_internal_identifiers_or_payment_information()
    {
        var (owner, quotation) = await NewQuotationAsync();

        var link = await CreateLinkDtoAsync(owner, quotation.Id);
        var share = link.Share;
        var everything = string.Join('\n',
            share.Url, share.Message, share.EmailSubject, share.WhatsAppUrl, share.MailtoUrl);

        everything.Should().NotContain(quotation.Id.ToString());
        everything.Should().NotContain(quotation.Customer.Id.ToString());
        everything.Should().NotContain(PublicTokenGenerator.Hash(TokenFrom(link.Url)));

        // A quotation is not a bill. Nothing here may invite or describe a payment.
        foreach (var word in new[] { "razorpay", "payment", "pay ", "outstanding", "order_id" })
            everything.ToLowerInvariant().Should().NotContain(word);
    }

    [Fact]
    public async Task Re_sharing_returns_material_for_the_new_link_and_retires_the_old_one()
    {
        var (owner, quotation) = await NewQuotationAsync();

        var first = await CreateLinkDtoAsync(owner, quotation.Id);
        var second = await CreateLinkDtoAsync(owner, quotation.Id);

        second.Url.Should().NotBe(first.Url);
        second.Share.Url.Should().Be(second.Url);
        second.Share.Message.Should().Contain(second.Url).And.NotContain(TokenFrom(first.Url));

        var anonymous = Anonymous(_factory);
        (await anonymous.GetAsync($"/api/public/quotations/{TokenFrom(first.Url)}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await anonymous.GetAsync($"/api/public/quotations/{TokenFrom(second.Url)}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_customer_with_no_phone_or_email_still_yields_a_usable_share()
    {
        var owner = await _factory.CreateSignedInClientAsync();
        var customer = await (await owner.PostAsJsonAsync("/api/customers", new { name = "Anon" }))
            .Content.ReadFromJsonAsync<CustomerDto>();
        var quotation = await (await owner.PostAsJsonAsync("/api/quotations", Payload(customer!.Id)))
            .Content.ReadFromJsonAsync<QuotationDto>();

        var share = (await CreateLinkDtoAsync(owner, quotation!.Id)).Share;

        share.CustomerPhone.Should().BeNull();
        share.CustomerEmail.Should().BeNull();
        share.WhatsAppUrl.Should().StartWith("https://wa.me/?text=");
        share.MailtoUrl.Should().StartWith("mailto:?subject=");
        share.Message.Should().Contain(share.Url);
    }

    [Fact]
    public async Task A_link_cannot_be_created_for_a_quotation_that_does_not_exist()
    {
        var owner = await _factory.CreateSignedInClientAsync();

        var response = await owner.PostAsync($"/api/quotations/{Guid.NewGuid()}/public-link", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
