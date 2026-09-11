using FluentAssertions;
using Quotely.Api.Models;
using Quotely.Api.Services;
using Xunit;

namespace Quotely.Tests;

/// <summary>
/// V2.4 — the message a customer receives and the deep links that carry it. Pure input-to-output,
/// so none of this needs a browser, WhatsApp or a mail client to be asserted on.
/// </summary>
public class InvoiceShareBuilderTests
{
    private const string Url = "https://quotely.app/i/PZ8rN1kQvT3mYhW6bL0xJdS4gFcA9eUiR2oK7nVpQwE";

    private static Invoice SampleInvoice(
        string customerName = "John Smith",
        string? phone = "+91 91234 56780",
        string? email = "john@example.com",
        string currency = "INR") => new()
        {
            Id = Guid.NewGuid(),
            InvoiceNumber = "INV-001024",
            DueDate = new DateOnly(2026, 9, 20),
            GrandTotal = 12980m,
            Currency = currency,
            CustomerName = customerName,
            CustomerPhone = phone,
            CustomerEmail = email
        };

    // ---- the message ----------------------------------------------------

    [Fact]
    public void The_message_carries_the_invoice_details_and_nothing_else()
    {
        var share = InvoiceShareBuilder.Build(SampleInvoice(), "ABC Electrical", 12980m, Url);

        share.Message.Should().Be(
            "Hi John,\n\n" +
            "Your invoice INV-001024 from ABC Electrical is ready.\n\n" +
            "Amount: ₹12,980.00\n" +
            "Due date: 20 Sep 2026\n\n" +
            "View and pay your invoice:\n" + Url + "\n\n" +
            "Thank you.");
    }

    [Fact]
    public void The_email_subject_names_the_invoice_and_the_business()
    {
        var share = InvoiceShareBuilder.Build(SampleInvoice(), "ABC Electrical", 12980m, Url);

        share.EmailSubject.Should().Be("Invoice INV-001024 from ABC Electrical");
    }

    [Fact]
    public void The_amount_shown_is_the_one_passed_in_not_the_invoice_total()
    {
        // The caller passes the outstanding balance; a part-paid invoice must ask for the rest.
        var share = InvoiceShareBuilder.Build(SampleInvoice(), "ABC Electrical", 4000m, Url);

        share.Message.Should().Contain("Amount: ₹4,000.00").And.NotContain("12,980");
    }

    [Theory]
    [InlineData("INR", "₹1,500.00")]
    [InlineData("USD", "$1,500.00")]
    [InlineData("EUR", "€1,500.00")]
    [InlineData("GBP", "£1,500.00")]
    [InlineData("AED", "AED 1,500.00")]
    public void Money_is_formatted_for_the_invoices_own_currency(string currency, string expected)
    {
        var share = InvoiceShareBuilder.Build(SampleInvoice(currency: currency), "ABC", 1500m, Url);

        share.Message.Should().Contain($"Amount: {expected}");
    }

    [Fact]
    public void A_single_word_customer_name_is_greeted_whole()
    {
        var share = InvoiceShareBuilder.Build(SampleInvoice("Priya"), "ABC", 100m, Url);

        share.Message.Should().StartWith("Hi Priya,");
    }

    // ---- encoding --------------------------------------------------------

    [Fact]
    public void Newlines_survive_encoding_and_decode_back_to_the_message()
    {
        var share = InvoiceShareBuilder.Build(SampleInvoice(), "ABC Electrical", 12980m, Url);

        var text = share.WhatsAppUrl["https://wa.me/919123456780?text=".Length..];

        text.Should().NotContain("\n", "a raw newline would break the URL");
        Uri.UnescapeDataString(text).Should().Be(share.Message);
    }

    [Fact]
    public void Characters_that_could_break_out_of_a_query_parameter_are_escaped()
    {
        // An ampersand would otherwise start a new parameter, and a hash would truncate the body.
        var invoice = SampleInvoice("Smith & Sons #1 <Ltd>");
        var share = InvoiceShareBuilder.Build(invoice, "Ohm & Watt", 100m, Url);

        share.WhatsAppUrl.Should().NotContain("&text").And.NotContain("#");
        share.MailtoUrl.Split('?')[1].Should().NotContain("#");

        var body = share.MailtoUrl.Split("&body=")[1];
        Uri.UnescapeDataString(body).Should().Be(share.Message);
    }

    [Fact]
    public void Non_latin_names_are_carried_intact()
    {
        var invoice = SampleInvoice("अरुण कुमार");
        var share = InvoiceShareBuilder.Build(invoice, "विद्युत सेवा", 100m, Url);

        share.Message.Should().StartWith("Hi अरुण,");
        Uri.UnescapeDataString(share.WhatsAppUrl).Should().Contain("विद्युत सेवा");
    }

    [Fact]
    public void The_subject_is_encoded_separately_from_the_body()
    {
        var share = InvoiceShareBuilder.Build(SampleInvoice(), "A & B Ltd", 100m, Url);

        var query = share.MailtoUrl.Split('?')[1];
        var parts = query.Split("&body=");

        parts.Should().HaveCount(2, "the subject must not leak an unescaped separator");
        Uri.UnescapeDataString(parts[0]["subject=".Length..]).Should().Be(share.EmailSubject);
    }

    // ---- missing contact details ------------------------------------------

    [Fact]
    public void A_customer_without_a_phone_gets_an_unaddressed_whatsapp_link()
    {
        var share = InvoiceShareBuilder.Build(SampleInvoice(phone: null), "ABC", 100m, Url);

        share.CustomerPhone.Should().BeNull();
        // WhatsApp will ask the owner to pick a contact rather than open an invalid number.
        share.WhatsAppUrl.Should().StartWith("https://wa.me/?text=");
        Uri.UnescapeDataString(share.WhatsAppUrl).Should().Contain(Url);
    }

    [Fact]
    public void A_customer_without_an_email_gets_an_unaddressed_draft()
    {
        var share = InvoiceShareBuilder.Build(SampleInvoice(email: null), "ABC", 100m, Url);

        share.CustomerEmail.Should().BeNull();
        share.MailtoUrl.Should().StartWith("mailto:?subject=");
    }

    [Fact]
    public void A_customer_with_neither_still_gets_a_usable_message_and_link()
    {
        var share = InvoiceShareBuilder.Build(SampleInvoice(phone: null, email: null), "ABC", 100m, Url);

        share.Url.Should().Be(Url);
        share.Message.Should().Contain(Url);
        share.WhatsAppUrl.Should().NotBeEmpty();
        share.MailtoUrl.Should().NotBeEmpty();
    }

    // ---- phone normalisation ----------------------------------------------

    [Theory]
    [InlineData("+91 91234 56780", "919123456780")]
    [InlineData("+91-91234-56780", "919123456780")]
    [InlineData("(044) 2811 1234", "04428111234")]
    [InlineData("00 91 9123456780", "00919123456780")]
    public void A_usable_number_is_reduced_to_digits(string stored, string expected)
    {
        InvoiceShareBuilder.NormalisePhone(stored).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("n/a")]
    [InlineData("1234")]                    // too short to be an international number
    [InlineData("12345678901234567890")]    // longer than E.164 permits
    public void An_unusable_number_is_treated_as_absent_rather_than_guessed_at(string? stored)
    {
        InvoiceShareBuilder.NormalisePhone(stored).Should().BeNull();
    }

    // ---- what must never appear -------------------------------------------

    [Fact]
    public void Nothing_internal_leaks_into_the_share_material()
    {
        var invoice = SampleInvoice();
        var share = InvoiceShareBuilder.Build(invoice, "ABC Electrical", 100m, Url);

        var everything = string.Join('\n', share.Message, share.WhatsAppUrl, share.MailtoUrl, share.EmailSubject);

        everything.Should().NotContain(invoice.Id.ToString());
        everything.Should().NotContain("razorpay", "no provider reference belongs in a customer message");
        Uri.UnescapeDataString(everything).Should().NotContain(invoice.Id.ToString());
    }
}
