using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Quotely.Api.Models;

namespace Quotely.Api.Pdf;

/// <summary>
/// Professional single- or multi-page invoice layout. Built entirely from the invoice's own
/// snapshot — the customer block, the line items and the totals are the stored values, not a
/// lookup against today's customer record or product catalogue. Only the letterhead comes from
/// the live business profile, since that is the issuer's own identity.
/// </summary>
public class InvoiceDocument : IDocument
{
    private const string Ink = "#111827";
    private const string Muted = "#6B7280";
    private const string Line = "#E5E7EB";
    private const string Accent = "#0F766E";
    private const string SoftAccent = "#ECFDF5";

    private readonly Invoice _invoice;
    private readonly BusinessProfile? _business;
    private readonly string _currency;
    private readonly byte[]? _logo;

    public InvoiceDocument(Invoice invoice, BusinessProfile? business, byte[]? logo = null)
    {
        _invoice = invoice;
        _business = business;
        // The currency stored on the invoice, so a later profile change cannot restate old money.
        _currency = invoice.Currency;
        _logo = logo;
    }

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Invoice {_invoice.InvoiceNumber}",
        Author = _business?.BusinessName ?? "Quotely",
        Subject = $"Invoice for {_invoice.CustomerName}"
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.DefaultTextStyle(t => t.FontSize(9.5f).FontColor(Ink).FontFamily(PdfFonts.Family));

            page.Header().Element(ComposeHeader);
            page.Content().PaddingVertical(16).Element(ComposeContent);
            page.Footer().Element(ComposeFooter);
        });
    }

    private void ComposeHeader(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    if (_logo is not null)
                        left.Item().PaddingBottom(6).Height(46).AlignLeft().Image(_logo).FitHeight();

                    left.Item().Text(_business?.BusinessName ?? "Your Business")
                        .FontSize(16).SemiBold().FontColor(Ink);

                    foreach (var addressLine in BusinessAddressLines())
                        left.Item().Text(addressLine).FontSize(9).FontColor(Muted);

                    if (!string.IsNullOrWhiteSpace(_business?.Phone))
                        left.Item().Text($"Phone: {_business!.Phone}").FontSize(9).FontColor(Muted);
                    if (!string.IsNullOrWhiteSpace(_business?.BusinessEmail))
                        left.Item().Text($"Email: {_business!.BusinessEmail}").FontSize(9).FontColor(Muted);
                    if (!string.IsNullOrWhiteSpace(_business?.TaxNumber))
                        left.Item().Text($"Tax / GST: {_business!.TaxNumber}").FontSize(9).FontColor(Muted);
                });

                row.ConstantItem(190).Column(right =>
                {
                    right.Item().AlignRight().Text("INVOICE")
                        .FontSize(24).Bold().FontColor(Accent).LetterSpacing(0.08f);
                    right.Item().AlignRight().PaddingTop(2).Text(_invoice.InvoiceNumber)
                        .FontSize(11).SemiBold().FontColor(Ink);
                    right.Item().AlignRight().PaddingTop(8)
                        .Text($"Invoice date: {FormatDate(_invoice.InvoiceDate)}").FontSize(9).FontColor(Muted);
                    right.Item().AlignRight()
                        .Text($"Due date: {FormatDate(_invoice.DueDate)}").FontSize(9).FontColor(Muted);

                    if (!string.IsNullOrWhiteSpace(_invoice.Quotation?.QuotationNumber))
                        right.Item().AlignRight()
                            .Text($"Quotation: {_invoice.Quotation!.QuotationNumber}").FontSize(9).FontColor(Muted);

                    right.Item().AlignRight().PaddingTop(6).Element(e => StatusBadge(e, _invoice.Status));
                });
            });

            column.Item().PaddingTop(12).LineHorizontal(1).LineColor(Line);
        });
    }

    private static void StatusBadge(IContainer container, InvoiceStatus status)
    {
        container.Background(SoftAccent).PaddingVertical(3).PaddingHorizontal(8)
            .Text(Humanize(status).ToUpperInvariant())
            .FontSize(7.5f).SemiBold().FontColor(Accent).LetterSpacing(0.06f);
    }

    /// <summary>"PartiallyPaid" prints as "Partially Paid".</summary>
    private static string Humanize(InvoiceStatus status) =>
        status == InvoiceStatus.PartiallyPaid ? "Partially Paid" : status.ToString();

    private void ComposeContent(IContainer container)
    {
        container.Column(column =>
        {
            column.Spacing(16);
            column.Item().Element(ComposeBillTo);
            column.Item().Element(ComposeItemsTable);
            column.Item().Element(ComposeTotals);
            column.Item().Element(ComposeNotes);
        });
    }

    private void ComposeBillTo(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().PaddingBottom(4).Text("BILL TO")
                .FontSize(8).SemiBold().FontColor(Muted).LetterSpacing(0.08f);

            column.Item().Text(_invoice.CustomerName).FontSize(11).SemiBold();

            if (!string.IsNullOrWhiteSpace(_invoice.CustomerCompanyName))
                column.Item().Text(_invoice.CustomerCompanyName!).FontSize(9.5f);

            foreach (var addressLine in CustomerAddressLines())
                column.Item().Text(addressLine).FontSize(9).FontColor(Muted);

            var contact = new[] { _invoice.CustomerPhone, _invoice.CustomerEmail }
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToArray();
            if (contact.Length > 0)
                column.Item().Text(string.Join("  •  ", contact)).FontSize(9).FontColor(Muted);
        });
    }

    private void ComposeItemsTable(IContainer container)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(4.2f);  // description
                columns.RelativeColumn(1.1f);  // qty
                columns.RelativeColumn(1.6f);  // unit price
                columns.RelativeColumn(1.4f);  // discount
                columns.RelativeColumn(1.0f);  // tax
                columns.RelativeColumn(1.8f);  // total
            });

            // Repeats on every page when items overflow.
            table.Header(header =>
            {
                HeaderCell(header.Cell(), "DESCRIPTION", Alignment.Left);
                HeaderCell(header.Cell(), "QTY", Alignment.Right);
                HeaderCell(header.Cell(), "UNIT PRICE", Alignment.Right);
                HeaderCell(header.Cell(), "DISCOUNT", Alignment.Right);
                HeaderCell(header.Cell(), "TAX", Alignment.Right);
                HeaderCell(header.Cell(), "TOTAL", Alignment.Right);
            });

            foreach (var item in _invoice.Items.OrderBy(i => i.SortOrder))
            {
                table.Cell().Element(BodyCell).Column(cell =>
                {
                    cell.Item().Text(item.Name).FontSize(9.5f).SemiBold();
                    if (!string.IsNullOrWhiteSpace(item.Description))
                        cell.Item().PaddingTop(1).Text(item.Description!).FontSize(8.5f).FontColor(Muted);
                });
                table.Cell().Element(BodyCell).AlignRight()
                    .Text($"{CurrencyFormatter.Quantity(item.Quantity)} {item.Unit}").FontSize(9.5f);
                table.Cell().Element(BodyCell).AlignRight()
                    .Text(CurrencyFormatter.Format(item.UnitPrice, _currency)).FontSize(9.5f);
                table.Cell().Element(BodyCell).AlignRight()
                    .Text(item.Discount > 0 ? CurrencyFormatter.Format(item.Discount, _currency) : "—").FontSize(9.5f);
                table.Cell().Element(BodyCell).AlignRight()
                    .Text(CurrencyFormatter.Percent(item.TaxRate)).FontSize(9.5f);
                table.Cell().Element(BodyCell).AlignRight()
                    .Text(CurrencyFormatter.Format(item.LineTotal, _currency)).FontSize(9.5f).SemiBold();
            }
        });

        static void HeaderCell(IContainer cell, string text, Alignment alignment)
        {
            var styled = cell.Background(SoftAccent).PaddingVertical(6).PaddingHorizontal(6);
            styled = alignment == Alignment.Right ? styled.AlignRight() : styled;
            styled.Text(text).FontSize(7.5f).SemiBold().FontColor(Accent).LetterSpacing(0.06f);
        }

        // ShowEntire keeps an item on one page instead of splitting its description across a break.
        static IContainer BodyCell(IContainer cell) =>
            cell.ShowEntire().BorderBottom(1).BorderColor(Line).PaddingVertical(7).PaddingHorizontal(6);
    }

    private enum Alignment { Left, Right }

    private void ComposeTotals(IContainer container)
    {
        container.AlignRight().Width(250).Column(column =>
        {
            TotalRow(column, "Subtotal", CurrencyFormatter.Format(_invoice.Subtotal, _currency));

            if (_invoice.DiscountTotal > 0)
                TotalRow(column, "Discount", $"-{CurrencyFormatter.Format(_invoice.DiscountTotal, _currency)}");

            TotalRow(column, "Tax", CurrencyFormatter.Format(_invoice.TaxTotal, _currency));

            column.Item().PaddingTop(6).LineHorizontal(1).LineColor(Line);

            column.Item().PaddingTop(8).Background(SoftAccent).Padding(10).Row(row =>
            {
                row.RelativeItem().Text("TOTAL DUE").FontSize(10).SemiBold().FontColor(Accent);
                row.RelativeItem().AlignRight()
                    .Text(CurrencyFormatter.Format(_invoice.GrandTotal, _currency))
                    .FontSize(13).Bold().FontColor(Accent);
            });
        });

        static void TotalRow(ColumnDescriptor column, string label, string value)
        {
            column.Item().PaddingVertical(3).Row(row =>
            {
                row.RelativeItem().Text(label).FontSize(9.5f).FontColor(Muted);
                row.RelativeItem().AlignRight().Text(value).FontSize(9.5f);
            });
        }
    }

    private void ComposeNotes(IContainer container)
    {
        container.Column(column =>
        {
            column.Spacing(12);

            if (!string.IsNullOrWhiteSpace(_invoice.Notes))
                column.Item().Element(e => Block(e, "NOTES", _invoice.Notes!));

            if (!string.IsNullOrWhiteSpace(_invoice.Terms))
                column.Item().Element(e => Block(e, "TERMS & CONDITIONS", _invoice.Terms!));
        });

        static void Block(IContainer container, string title, string body)
        {
            container.Column(column =>
            {
                column.Item().PaddingBottom(3).Text(title)
                    .FontSize(8).SemiBold().FontColor(Muted).LetterSpacing(0.08f);
                column.Item().Text(body).FontSize(9).LineHeight(1.35f);
            });
        }
    }

    private void ComposeFooter(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().PaddingBottom(6).LineHorizontal(1).LineColor(Line);
            column.Item().Row(row =>
            {
                row.RelativeItem().Text(_business?.BusinessName ?? "Quotely")
                    .FontSize(8).FontColor(Muted);
                row.RelativeItem().AlignRight().Text(text =>
                {
                    text.DefaultTextStyle(t => t.FontSize(8).FontColor(Muted));
                    text.Span("Page ");
                    text.CurrentPageNumber();
                    text.Span(" of ");
                    text.TotalPages();
                });
            });
        });
    }

    private IEnumerable<string> BusinessAddressLines()
    {
        if (!string.IsNullOrWhiteSpace(_business?.AddressLine)) yield return _business!.AddressLine!;
        var locality = Join(_business?.City, _business?.State, _business?.PostalCode);
        if (locality is not null) yield return locality;
        if (!string.IsNullOrWhiteSpace(_business?.Country)) yield return _business!.Country!;
    }

    private IEnumerable<string> CustomerAddressLines()
    {
        if (!string.IsNullOrWhiteSpace(_invoice.CustomerAddressLine)) yield return _invoice.CustomerAddressLine!;
        var locality = Join(_invoice.CustomerCity, _invoice.CustomerState, _invoice.CustomerPostalCode);
        if (locality is not null) yield return locality;
        if (!string.IsNullOrWhiteSpace(_invoice.CustomerCountry)) yield return _invoice.CustomerCountry!;
    }

    private static string? Join(params string?[] parts)
    {
        var joined = string.Join(", ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        return string.IsNullOrWhiteSpace(joined) ? null : joined;
    }

    private static string FormatDate(DateOnly date) => date.ToString("dd MMM yyyy");
}
