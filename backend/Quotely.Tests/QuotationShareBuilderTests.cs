using FluentAssertions;
using Quotely.Api.Models;
using Quotely.Api.Services;
using Xunit;

namespace Quotely.Tests;

/// <summary>
/// V2.6 — the message a customer receives with a quotation and the deep links that carry it.
/// Pure input-to-output, so none of this needs a browser, WhatsApp or a mail client to assert on.
/// The invoice counterpart is <see cref="InvoiceShareBuilderTests"/>; both now sit on the same
/// <see cref="ShareComposer"/>, and these tests exist so the two cannot quietly diverge.
/// </summary>
public class QuotationShareBuilderTests
{
    private const string Url = "https://quotely.app/q/PZ8rN1kQvT3mYhW6bL0xJdS4gFcA9eUiR2oK7nVpQwE";

    private static Quotation SampleQuotation(decimal total = 25000m) => new()
    {
        Id = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        QuotationNumber = "QT-000001",
        QuotationDate = new DateOnly(2026, 9, 1),
        ValidUntil = new DateOnly(2026, 9, 30),
        GrandTotal = total,
        Subtotal = total
    };

    private static Customer SampleCustomer(
        string name = "John Smith",
        string? phone = "+91 91234 56780",
        string? email = "john@example.com") => new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Phone = phone,
            Email = email
        };

    // ---- the message ----------------------------------------------------

    [Fact]
    public void The_message_carries_the_quotation_details_and_nothing_else()
    {
        var share = QuotationShareBuilder.Build(
            SampleQuotation(), SampleCustomer(), "ABC Services", "INR", Url);

        share.Message.Should().Be(
            "Hi John,\n\n" +
            "Please find quotation QT-000001 from ABC Services.\n\n" +
            "Total: ₹25,000.00\n" +
            "Valid until: 30 Sep 2026\n\n" +
            "View quotation:\n" + Url + "\n\n" +
            "Thank you.");
    }

    [Fact]
    public void The_email_subject_names_the_quotation_and_the_business()
    {
        var share = QuotationShareBuilder.Build(
            SampleQuotation(), SampleCustomer(), "ABC Services", "INR", Url);

        share.EmailSubject.Should().Be("Quotation QT-000001 from ABC Services");
    }

    [Fact]
    public void The_total_comes_from_the_stored_quotation_not_from_a_caller_supplied_figure()
    {
        // Unlike an invoice — where the caller passes the outstanding balance — a quotation is
        // worth exactly what the server calculated and stored. There is no amount parameter.
        var share = QuotationShareBuilder.Build(
            SampleQuotation(total: 48250.50m), SampleCustomer(), "ABC Services", "INR", Url);

        share.Message.Should().Contain("Total: ₹48,250.50");
    }

    [Theory]
    [InlineData("INR", "₹1,500.00")]
    [InlineData("USD", "$1,500.00")]
    [InlineData("EUR", "€1,500.00")]
    [InlineData("GBP", "£1,500.00")]
    [InlineData("AED", "AED 1,500.00")]
    public void Money_is_formatted_in_the_businesss_own_currency(string currency, string expected)
    {
        var share = QuotationShareBuilder.Build(
            SampleQuotation(1500m), SampleCustomer(), "ABC", currency, Url);

        share.Message.Should().Contain($"Total: {expected}");
    }

    [Fact]
    public void A_single_word_customer_name_is_greeted_whole()
    {
        var share = QuotationShareBuilder.Build(
            SampleQuotation(), SampleCustomer(name: "Priya"), "ABC Services", "INR", Url);

        share.Message.Should().StartWith("Hi Priya,");
    }

    [Fact]
    public void A_quotation_whose_customer_record_is_missing_still_produces_a_usable_message()
    {
        var share = QuotationShareBuilder.Build(SampleQuotation(), null, "ABC Services", "INR", Url);

        share.Message.Should().StartWith("Hi there,").And.Contain(Url);
        share.WhatsAppUrl.Should().StartWith("https://wa.me/?text=");
        share.MailtoUrl.Should().StartWith("mailto:?subject=");
    }

    // ---- WhatsApp -------------------------------------------------------

    [Fact]
    public void The_whatsapp_link_addresses_the_customer_and_carries_the_message_encoded()
    {
        var share = QuotationShareBuilder.Build(
            SampleQuotation(), SampleCustomer(), "ABC Services", "INR", Url);

        share.WhatsAppUrl.Should().StartWith("https://wa.me/919123456780?text=");

        var text = share.WhatsAppUrl["https://wa.me/919123456780?text=".Length..];
        Uri.UnescapeDataString(text).Should().Be(share.Message,
            "the link must decode back to exactly what was previewed");
    }

    [Fact]
    public void Newlines_and_ampersands_cannot_break_out_of_the_query_parameter()
    {
        var share = QuotationShareBuilder.Build(
            SampleQuotation(), SampleCustomer(name: "Ram & Co"), "Smith & Sons", "INR", Url);

        // A literal & or newline in the query would truncate the message or inject a parameter.
        var query = share.WhatsAppUrl[(share.WhatsAppUrl.IndexOf('?') + 1)..];
        query.Should().NotContain("\n");
        query.Split('&').Should().ContainSingle("nothing may introduce a second query parameter");
    }

    [Fact]
    public void Non_latin_names_survive_intact()
    {
        var share = QuotationShareBuilder.Build(
            SampleQuotation(), SampleCustomer(name: "अश्विन"), "क्वोटली", "INR", Url);

        var text = share.WhatsAppUrl[(share.WhatsAppUrl.IndexOf('?') + 1)..]["text=".Length..];
        Uri.UnescapeDataString(text).Should().Contain("अश्विन").And.Contain("क्वोटली");
    }

    [Theory]
    [InlineData("+91 91234 56780", "919123456780")]
    [InlineData("091234-56780", "09123456780")]
    [InlineData("(044) 2811 9000", "04428119000")]
    public void A_usable_number_is_reduced_to_digits(string stored, string expected)
    {
        var share = QuotationShareBuilder.Build(
            SampleQuotation(), SampleCustomer(phone: stored), "ABC", "INR", Url);

        share.CustomerPhone.Should().Be(expected);
        share.WhatsAppUrl.Should().StartWith($"https://wa.me/{expected}?text=");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12345")]                       // too short to be an international number
    [InlineData("1234567890123456")]            // longer than E.164 permits
    [InlineData("call the office")]
    public void An_unusable_number_never_produces_a_link_to_a_wrong_recipient(string? stored)
    {
        var share = QuotationShareBuilder.Build(
            SampleQuotation(), SampleCustomer(phone: stored), "ABC", "INR", Url);

        share.CustomerPhone.Should().BeNull();
        // Unaddressed, so WhatsApp asks the owner to pick a contact rather than opening a chat
        // with a number the digits happened to form.
        share.WhatsAppUrl.Should().StartWith("https://wa.me/?text=");
    }

    // ---- email ----------------------------------------------------------

    [Fact]
    public void The_mailto_addresses_the_customer_with_an_encoded_subject_and_body()
    {
        var share = QuotationShareBuilder.Build(
            SampleQuotation(), SampleCustomer(), "ABC Services", "INR", Url);

        share.MailtoUrl.Should().StartWith("mailto:john%40example.com?subject=");

        var query = share.MailtoUrl[(share.MailtoUrl.IndexOf('?') + 1)..];
        var parts = query.Split('&');
        parts.Should().HaveCount(2, "only subject and body may appear");
        Uri.UnescapeDataString(parts[0]["subject=".Length..]).Should().Be(share.EmailSubject);
        Uri.UnescapeDataString(parts[1]["body=".Length..]).Should().Be(share.Message);
    }

    [Fact]
    public void A_customer_without_an_email_gets_an_unaddressed_draft()
    {
        var share = QuotationShareBuilder.Build(
            SampleQuotation(), SampleCustomer(email: null), "ABC Services", "INR", Url);

        share.CustomerEmail.Should().BeNull();
        share.MailtoUrl.Should().StartWith("mailto:?subject=");
    }

    // ---- what must never appear ----------------------------------------

    [Fact]
    public void Nothing_internal_leaks_into_the_share_material()
    {
        var quotation = SampleQuotation();
        var customer = SampleCustomer();

        var share = QuotationShareBuilder.Build(quotation, customer, "ABC Services", "INR", Url);

        var everything = string.Join('\n',
            share.Url, share.Message, share.EmailSubject, share.WhatsAppUrl, share.MailtoUrl);

        everything.Should().NotContain(quotation.Id.ToString());
        everything.Should().NotContain(quotation.UserId.ToString());
        everything.Should().NotContain(customer.Id.ToString());
        // The public URL is the only identifier a customer ever receives.
        share.Url.Should().Be(Url);
    }
}
