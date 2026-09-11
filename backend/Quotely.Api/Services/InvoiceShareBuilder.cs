using System.Globalization;
using System.Text;
using Quotely.Api.DTOs;
using Quotely.Api.Models;

namespace Quotely.Api.Services;

/// <summary>
/// Composes the message a business owner sends their customer along with a payment link, and the
/// WhatsApp / mail-client URLs that carry it.
///
/// This is deliberately server-side and pure. The wording and the figures are the business's own
/// financial communication, so they are assembled where the authoritative numbers already live
/// rather than stitched together in the browser from values it happens to be holding — and being a
/// function of its inputs, it can be asserted on directly in the test suite.
///
/// Quotely sends nothing. These are deep links: WhatsApp and the customer's mail client do the
/// sending, with the owner in the loop. There is no WhatsApp Business API and no mail transport
/// anywhere in this milestone.
/// </summary>
public static class InvoiceShareBuilder
{
    /// <summary>
    /// Builds the share payload for an invoice whose link has just been minted.
    /// </summary>
    /// <param name="invoice">The invoice, read for its own snapshot rather than live records.</param>
    /// <param name="businessName">The issuing business's name, as it should appear to the customer.</param>
    /// <param name="amount">What the customer owes right now — the outstanding balance.</param>
    /// <param name="url">The public invoice URL. The only identifier that may appear in a message.</param>
    public static InvoiceShareDto Build(Invoice invoice, string businessName, decimal amount, string url)
    {
        var greetingName = FirstName(invoice.CustomerName);
        var money = FormatMoney(amount, invoice.Currency);
        var due = FormatDate(invoice.DueDate);

        var message = new StringBuilder()
            .Append("Hi ").Append(greetingName).Append(",\n\n")
            .Append("Your invoice ").Append(invoice.InvoiceNumber)
            .Append(" from ").Append(businessName).Append(" is ready.\n\n")
            .Append("Amount: ").Append(money).Append('\n')
            .Append("Due date: ").Append(due).Append("\n\n")
            .Append("View and pay your invoice:\n").Append(url).Append("\n\n")
            .Append("Thank you.")
            .ToString();

        var subject = $"Invoice {invoice.InvoiceNumber} from {businessName}";

        var phone = NormalisePhone(invoice.CustomerPhone);
        var email = string.IsNullOrWhiteSpace(invoice.CustomerEmail) ? null : invoice.CustomerEmail.Trim();

        return new InvoiceShareDto
        {
            Url = url,
            Message = message,
            EmailSubject = subject,
            CustomerPhone = phone,
            CustomerEmail = email,
            // wa.me resolves to the native app on a phone and to WhatsApp Web on a desktop, so one
            // URL covers both. Without a usable number we still offer the message — WhatsApp asks
            // the owner to choose a contact — rather than building a link to an invalid number.
            WhatsAppUrl = phone is null
                ? $"https://wa.me/?text={Encode(message)}"
                : $"https://wa.me/{phone}?text={Encode(message)}",
            // A mailto with no recipient still opens a composed draft for the owner to address,
            // which is the graceful answer when the customer record has no email on file.
            MailtoUrl = email is null
                ? $"mailto:?subject={Encode(subject)}&body={Encode(message)}"
                : $"mailto:{Encode(email)}?subject={Encode(subject)}&body={Encode(message)}"
        };
    }

    /// <summary>
    /// Percent-encoding for a URL query value. <see cref="Uri.EscapeDataString"/> leaves no
    /// character that could terminate the parameter or inject another, so newlines, ampersands and
    /// non-Latin scripts in a customer or business name all survive intact.
    /// </summary>
    private static string Encode(string value) => Uri.EscapeDataString(value);

    /// <summary>
    /// Reduces a stored phone number to the digits WhatsApp expects: country code and subscriber
    /// number, no plus, no spaces. Anything that cannot plausibly be an international number is
    /// treated as absent rather than guessed at.
    /// </summary>
    public static string? NormalisePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;

        var digits = new string(phone.Where(char.IsAsciiDigit).ToArray());

        // Shortest plausible international number is 8 digits; longest permitted by E.164 is 15.
        return digits.Length is >= 8 and <= 15 ? digits : null;
    }

    /// <summary>The name a person is greeted by, falling back to the whole stored name.</summary>
    private static string FirstName(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0) return "there";
        var space = trimmed.IndexOf(' ');
        return space > 0 ? trimmed[..space] : trimmed;
    }

    /// <summary>
    /// Matches how the app renders money elsewhere: symbol where we know one, ISO code otherwise.
    /// </summary>
    private static string FormatMoney(decimal amount, string currency)
    {
        var symbol = currency.ToUpperInvariant() switch
        {
            "INR" => "₹",
            "USD" => "$",
            "EUR" => "€",
            "GBP" => "£",
            _ => currency.ToUpperInvariant() + " "
        };
        return symbol + amount.ToString("N2", CultureInfo.InvariantCulture);
    }

    private static string FormatDate(DateOnly date) =>
        date.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
}
