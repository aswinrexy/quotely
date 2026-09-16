using System.Text;
using Quotely.Api.DTOs;
using Quotely.Api.Models;

namespace Quotely.Api.Services;

/// <summary>
/// Composes the message a business owner sends their customer along with a payment link.
///
/// This is deliberately server-side and pure. The wording and the figures are the business's own
/// financial communication, so they are assembled where the authoritative numbers already live
/// rather than stitched together in the browser from values it happens to be holding — and being a
/// function of its inputs, it can be asserted on directly in the test suite.
///
/// Everything mechanical around the words — greeting, money formatting, the WhatsApp deep link and
/// the mailto draft — comes from <see cref="ShareComposer"/>, shared with
/// <see cref="QuotationShareBuilder"/>.
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
    public static ShareDto Build(Invoice invoice, string businessName, decimal amount, string url)
    {
        var greetingName = ShareComposer.FirstName(invoice.CustomerName);
        var money = ShareComposer.FormatMoney(amount, invoice.Currency);
        var due = ShareComposer.FormatDate(invoice.DueDate);

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

        return ShareComposer.Compose(url, message, subject, invoice.CustomerPhone, invoice.CustomerEmail);
    }

    /// <summary>
    /// Kept as the invoice-facing name for <see cref="ShareComposer.NormalisePhone"/>, which is now
    /// where the rule lives.
    /// </summary>
    public static string? NormalisePhone(string? phone) => ShareComposer.NormalisePhone(phone);
}
