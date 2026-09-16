using System.Globalization;
using Quotely.Api.DTOs;

namespace Quotely.Api.Services;

/// <summary>
/// The mechanics every document share has in common: how a person is greeted, how money and dates
/// are written, how a phone number is reduced to something WhatsApp accepts, and how a finished
/// message becomes a WhatsApp deep link and a mailto draft.
///
/// Invoices (V2.4) and quotations (V2.6) differ only in what the message says. Everything around
/// the words is identical, so it lives here once — a second copy is how the two documents would
/// eventually start formatting the same rupee amount two different ways.
///
/// Quotely sends nothing. These are deep links: WhatsApp and the customer's mail client do the
/// sending, with the owner in the loop. There is no WhatsApp Business API and no mail transport
/// anywhere in this application.
/// </summary>
public static class ShareComposer
{
    /// <summary>
    /// Wraps a composed message in the three ways it can be handed over.
    /// </summary>
    /// <param name="url">The public document URL. The only identifier that may appear in a message.</param>
    /// <param name="message">The body, already written by the calling builder.</param>
    /// <param name="subject">The email subject line.</param>
    /// <param name="phone">The customer's raw stored phone number, or null.</param>
    /// <param name="email">The customer's raw stored email address, or null.</param>
    public static ShareDto Compose(string url, string message, string subject, string? phone, string? email)
    {
        var digits = NormalisePhone(phone);
        var address = string.IsNullOrWhiteSpace(email) ? null : email.Trim();

        return new ShareDto
        {
            Url = url,
            Message = message,
            EmailSubject = subject,
            CustomerPhone = digits,
            CustomerEmail = address,
            // wa.me resolves to the native app on a phone and to WhatsApp Web on a desktop, so one
            // URL covers both. Without a usable number we still offer the message — WhatsApp asks
            // the owner to choose a contact — rather than building a link to an invalid number.
            WhatsAppUrl = digits is null
                ? $"https://wa.me/?text={Encode(message)}"
                : $"https://wa.me/{digits}?text={Encode(message)}",
            // A mailto with no recipient still opens a composed draft for the owner to address,
            // which is the graceful answer when the customer record has no email on file.
            MailtoUrl = address is null
                ? $"mailto:?subject={Encode(subject)}&body={Encode(message)}"
                : $"mailto:{Encode(address)}?subject={Encode(subject)}&body={Encode(message)}"
        };
    }

    /// <summary>
    /// Percent-encoding for a URL query value. <see cref="Uri.EscapeDataString"/> leaves no
    /// character that could terminate the parameter or inject another, so newlines, ampersands and
    /// non-Latin scripts in a customer or business name all survive intact.
    /// </summary>
    public static string Encode(string value) => Uri.EscapeDataString(value);

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
    public static string FirstName(string? name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0) return "there";
        var space = trimmed.IndexOf(' ');
        return space > 0 ? trimmed[..space] : trimmed;
    }

    /// <summary>
    /// Matches how the app renders money elsewhere: symbol where we know one, ISO code otherwise.
    /// </summary>
    public static string FormatMoney(decimal amount, string currency)
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

    public static string FormatDate(DateOnly date) =>
        date.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
}
