using System.Text;
using Quotely.Api.DTOs;
using Quotely.Api.Models;

namespace Quotely.Api.Services;

/// <summary>
/// Composes the message a business owner sends a customer along with a quotation link.
///
/// The counterpart to <see cref="InvoiceShareBuilder"/>, and deliberately the same shape: both are
/// pure functions of authoritative server-side records, so both can be asserted on directly in the
/// test suite, and neither lets the browser decide what a quotation is worth.
///
/// The difference between the two messages is only in the words. An invoice asks to be paid by a
/// date; a quotation asks to be reviewed before it lapses. Everything mechanical — greeting,
/// money formatting, WhatsApp and mailto construction — comes from <see cref="ShareComposer"/>.
/// </summary>
public static class QuotationShareBuilder
{
    /// <summary>
    /// Builds the share payload for a quotation whose link has just been minted.
    /// </summary>
    /// <param name="quotation">The quotation, read for its own stored, server-calculated totals.</param>
    /// <param name="customer">The customer record the quotation was raised for, if still present.</param>
    /// <param name="businessName">The issuing business's name, as it should appear to the customer.</param>
    /// <param name="currency">The business's currency, the same one the document is rendered in.</param>
    /// <param name="url">The public quotation URL. The only identifier that may appear in a message.</param>
    public static ShareDto Build(
        Quotation quotation, Customer? customer, string businessName, string currency, string url)
    {
        var greetingName = ShareComposer.FirstName(customer?.Name);
        var total = ShareComposer.FormatMoney(quotation.GrandTotal, currency);
        var validUntil = ShareComposer.FormatDate(quotation.ValidUntil);

        var message = new StringBuilder()
            .Append("Hi ").Append(greetingName).Append(",\n\n")
            .Append("Please find quotation ").Append(quotation.QuotationNumber)
            .Append(" from ").Append(businessName).Append(".\n\n")
            .Append("Total: ").Append(total).Append('\n')
            .Append("Valid until: ").Append(validUntil).Append("\n\n")
            .Append("View quotation:\n").Append(url).Append("\n\n")
            .Append("Thank you.")
            .ToString();

        var subject = $"Quotation {quotation.QuotationNumber} from {businessName}";

        return ShareComposer.Compose(url, message, subject, customer?.Phone, customer?.Email);
    }
}
