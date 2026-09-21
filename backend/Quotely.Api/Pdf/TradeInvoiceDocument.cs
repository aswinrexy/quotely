using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Quotely.Api.Models;
using Quotely.Api.Services;

namespace Quotely.Api.Pdf;

/// <summary>
/// The import/export document: proforma or commercial invoice.
///
/// A ruled grid rather than Quotely's usual airy invoice layout, and that is a deliberate
/// departure. This document is read by a customs officer, a freight forwarder and a bank, often as
/// a fax-quality photocopy, and the boxes are how those readers find a field. The information
/// groups and their order follow the reference document the feature was built from; the
/// typography, rules and spacing are Quotely's.
///
/// Everything printed is the invoice's own snapshot. Only the letterhead is read from the live
/// business profile, exactly as the domestic invoice does.
/// </summary>
public class TradeInvoiceDocument : IDocument
{
    private const string Ink = "#111827";
    private const string Muted = "#6B7280";
    private const string Rule = "#9CA3AF";
    private const string HeaderFill = "#F3F4F6";

    private const float Border = 0.75f;

    private readonly Invoice _invoice;
    private readonly TradeInvoiceDetails _details;
    private readonly BusinessProfile? _business;
    private readonly byte[]? _logo;
    private readonly string _currency;

    public TradeInvoiceDocument(Invoice invoice, BusinessProfile? business, byte[]? logo = null)
    {
        _invoice = invoice;
        _details = invoice.TradeDetails
                   ?? throw new InvalidOperationException(
                       "A trade document cannot be rendered without its import/export detail.");
        _business = business;
        _logo = logo;
        _currency = invoice.Currency;
    }

    private bool IsExport => _details.TradeType == TradeType.Export;

    /// <summary>The issuing party's label flips with the direction of trade; the storage does not.</summary>
    private string PartyLabel => IsExport ? "Exporter" : "Importer";
    private string CounterpartyLabel => IsExport ? "Consignee" : "Supplier / Exporter";
    private string DestinationCountryLabel =>
        IsExport ? "Country of final destination" : "Country of import";

    private string Title => _details.DocumentType == TradeDocumentType.CommercialInvoice
        ? "COMMERCIAL INVOICE"
        : "PROFORMA INVOICE";

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"{Title} {DocumentNumber}",
        Author = _business?.BusinessName ?? _details.PartyName,
        Subject = $"{Title} for {_details.ConsigneeName}",
    };

    private string DocumentNumber => string.IsNullOrWhiteSpace(_details.DocumentNumber)
        ? _invoice.InvoiceNumber
        : _details.DocumentNumber!;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(26);
            page.DefaultTextStyle(t => t.FontSize(8f).FontColor(Ink).FontFamily(PdfFonts.Family));

            page.Content().Element(ComposeBody);
            page.Footer().Element(ComposeFooter);
        });
    }

    private void ComposeBody(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().Element(ComposeTitleBlock);
            column.Item().Element(ComposePartiesBlock);
            column.Item().Element(ComposeIdentifiersBlock);
            column.Item().Element(ComposeGoodsTable);

            // The closing blocks are kept whole and kept together. Without this a long consignment
            // pushes them over a page boundary and the document ends on a near-empty sheet
            // carrying nothing but the bottom edge of a box and the words "Authorised Signatory" —
            // which is where a signature would then have to go.
            column.Item().ShowEntire().Column(closing =>
            {
                closing.Item().Element(ComposeTotalsBlock);
                closing.Item().Element(ComposeAmountInWords);
                closing.Item().Element(ComposeDeclarationBlock);
            });
        });
    }

    // ---- title + declarations -------------------------------------------

    private void ComposeTitleBlock(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().Border(Border).BorderColor(Rule).Background(HeaderFill)
                .PaddingVertical(5).AlignCenter()
                .Text(Title).FontSize(14).Bold().LetterSpacing(0.08f);

            // The header declarations are the business's own words. They print above the document
            // body because that is where a customs reader looks for them.
            foreach (var line in SplitLines(_details.HeaderDeclarations))
            {
                column.Item().BorderHorizontal(Border).BorderColor(Rule)
                    .PaddingVertical(3).PaddingHorizontal(6).AlignCenter()
                    .Text($"“{line}”").FontSize(7.5f).SemiBold();
            }
        });
    }

    // ---- parties + logistics --------------------------------------------

    private void ComposePartiesBlock(IContainer container)
    {
        container.Border(Border).BorderColor(Rule).Row(row =>
        {
            // Left: the two address blocks, stacked, exactly as the reference arranges them.
            row.RelativeItem(42).Column(left =>
            {
                left.Item().Element(c => AddressCell(c, $"{PartyLabel}:", _details.PartyName, _details.PartyAddress, logo: true));
                left.Item().BorderTop(Border).BorderColor(Rule)
                    .Element(c => AddressCell(c, $"{CounterpartyLabel}:", _details.ConsigneeName, _details.ConsigneeAddress));
            });

            // Right: the document references and the shipment, in paired cells.
            row.RelativeItem(58).BorderLeft(Border).BorderColor(Rule).Column(right =>
            {
                right.Item().Row(r =>
                {
                    r.RelativeItem().Element(c => LabelledCell(c, "P-Invoice No", DocumentNumber, strong: true));
                    r.RelativeItem().BorderLeft(Border).BorderColor(Rule)
                        .Element(c => LabelledCell(c, "Date", FormatDate(_invoice.InvoiceDate), strong: true));
                });

                right.Item().BorderTop(Border).BorderColor(Rule).Row(r =>
                {
                    r.RelativeItem().Element(c => LabelledCell(c, "Buyer's Order No. & Date", BuyerOrderText()));
                    r.RelativeItem().BorderLeft(Border).BorderColor(Rule)
                        .Element(c => LabelledCell(c, "Other Reference(s)", _details.OtherReferences));
                });

                right.Item().BorderTop(Border).BorderColor(Rule).Row(r =>
                {
                    r.RelativeItem().Element(c => LabelledCell(c, "Pre-carriage by", _details.PreCarriageBy));
                    r.RelativeItem().BorderLeft(Border).BorderColor(Rule)
                        .Element(c => LabelledCell(c, "Place of Receipt by pre-carrier", _details.PlaceOfReceipt));
                });

                right.Item().BorderTop(Border).BorderColor(Rule).Row(r =>
                {
                    r.RelativeItem().Element(c => LabelledCell(c, "Vessel / Flight No.", _details.VesselOrFlightNumber, strong: true));
                    r.RelativeItem().BorderLeft(Border).BorderColor(Rule)
                        .Element(c => LabelledCell(c, "Port of Loading", _details.PortOfLoading, strong: true));
                });

                right.Item().BorderTop(Border).BorderColor(Rule).Row(r =>
                {
                    r.RelativeItem().Element(c => LabelledCell(c, "Airport / Port of Discharge", _details.PortOfDischarge, strong: true));
                    r.RelativeItem().BorderLeft(Border).BorderColor(Rule)
                        .Element(c => LabelledCell(c, "Final destination", _details.FinalDestination, strong: true));
                });

                right.Item().BorderTop(Border).BorderColor(Rule).Row(r =>
                {
                    r.RelativeItem().Element(c => InlineCell(c, "Country of origin of goods:", _details.CountryOfOrigin));
                    r.RelativeItem().BorderLeft(Border).BorderColor(Rule)
                        .Element(c => InlineCell(c, $"{DestinationCountryLabel}:", _details.CountryOfFinalDestination));
                });

                right.Item().BorderTop(Border).BorderColor(Rule)
                    .Element(c => InlineCell(c, "Terms of delivery & payment:", TermsText()));

                // Only printed when the business actually carries an APEDA registration. An empty
                // regulatory row on an exported document invites the question "why is this blank?".
                if (!string.IsNullOrWhiteSpace(_details.ApedaRegistrationNumber))
                {
                    right.Item().BorderTop(Border).BorderColor(Rule)
                        .Element(c => InlineCell(c, "APEDA registration number:", ApedaText()));
                }
            });
        });
    }

    private void ComposeIdentifiersBlock(IContainer container)
    {
        container.BorderHorizontal(Border).BorderVertical(Border).BorderColor(Rule).Row(row =>
        {
            row.RelativeItem(30).Element(c => BuyerCell(c));
            row.RelativeItem(40).BorderLeft(Border).BorderColor(Rule)
                .Element(c => AddressCell(c, "Notify party:", _details.NotifyPartyName, _details.NotifyPartyAddress));
            row.RelativeItem(30).BorderLeft(Border).BorderColor(Rule).Element(ComposeRegistrationNumbers);
        });
    }

    private void BuyerCell(IContainer container)
    {
        if (_details.BuyerSameAsConsignee)
        {
            container.Padding(5).Text(text =>
            {
                text.Span("Buyer (if other than consignee): ").FontSize(7).FontColor(Muted);
                text.Span($"Same as {CounterpartyLabel.ToLowerInvariant()}").SemiBold();
            });
            return;
        }

        AddressCell(container, "Buyer (if other than consignee):", _details.BuyerName, _details.BuyerAddress);
    }

    private void ComposeRegistrationNumbers(IContainer container)
    {
        container.Padding(5).Column(column =>
        {
            column.Spacing(1.5f);
            var any = false;

            foreach (var (label, value) in new[]
                     {
                         ("IEC No.", _details.IecNumber),
                         ("GST No.", _details.GstNumber),
                         ("PAN No.", _details.PanNumber),
                     })
            {
                if (string.IsNullOrWhiteSpace(value)) continue;
                any = true;
                column.Item().Text(text =>
                {
                    text.Span($"{label}: ").FontSize(7).FontColor(Muted);
                    text.Span(value!).SemiBold();
                });
            }

            // The cell has to exist to keep the three-column rule intact, so give it something
            // rather than leaving a silently empty box next to two full ones.
            if (!any) column.Item().Text("—").FontColor(Muted);
        });
    }

    // ---- goods -----------------------------------------------------------

    private void ComposeGoodsTable(IContainer container)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(7);    // Marks & Nos.
                columns.RelativeColumn(24);   // Description of goods
                columns.RelativeColumn(10);   // Dimension
                columns.RelativeColumn(10);   // HS code
                columns.RelativeColumn(9);    // Net weight
                columns.RelativeColumn(9);    // Gross weight
                columns.RelativeColumn(9);    // Quantity
                columns.RelativeColumn(11);   // Rate
                columns.RelativeColumn(13);   // Amount
            });

            // Repeated on every page: a goods table that spills over must not leave the second
            // page's columns unlabelled for whoever is checking the consignment against it.
            table.Header(header =>
            {
                HeaderCell(header.Cell(), "Marks &\nNos.");
                HeaderCell(header.Cell(), "Description of Goods", align: HorizontalAlignment.Left);
                HeaderCell(header.Cell(), "DIMENSION");
                HeaderCell(header.Cell(), "HS CODE");
                HeaderCell(header.Cell(), $"TOTAL\nNET WT");
                HeaderCell(header.Cell(), $"TOTAL\nGROSS WT");
                HeaderCell(header.Cell(), "Quantity");
                HeaderCell(header.Cell(), RateHeading());
                HeaderCell(header.Cell(), AmountHeading());
            });

            foreach (var item in _invoice.Items.OrderBy(i => i.SortOrder))
            {
                var trade = item.TradeDetails;

                BodyCell(table.Cell(), trade?.MarksAndNumbers);
                table.Cell().Element(BodyBox).Column(c =>
                {
                    c.Item().Text(item.Name).SemiBold();
                    if (!string.IsNullOrWhiteSpace(item.Description))
                        c.Item().Text(item.Description!).FontSize(7).FontColor(Muted);
                });
                BodyCell(table.Cell(), trade?.Dimension);
                BodyCell(table.Cell(), trade?.HsCode);
                BodyCell(table.Cell(), Weight(trade?.NetWeight), HorizontalAlignment.Right);
                BodyCell(table.Cell(), Weight(trade?.GrossWeight), HorizontalAlignment.Right);
                BodyCell(table.Cell(), QuantityText(item), HorizontalAlignment.Right);
                BodyCell(table.Cell(), Money(item.UnitPrice), HorizontalAlignment.Right);
                BodyCell(table.Cell(), Money(item.LineTotal), HorizontalAlignment.Right);
            }
        });
    }

    /// <summary>
    /// The rate column heading, e.g. "Rate/Kg C&amp;F INR".
    ///
    /// Taken from the line's own label when the business supplied one, because the reference shows
    /// an exporter whose label does not describe the arithmetic and that is their document to word.
    /// The maths never reads this — see <see cref="TradeRateBasis"/>.
    /// </summary>
    private string RateHeading()
    {
        var label = _invoice.Items.Select(i => i.TradeDetails?.RateLabel)
            .FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));

        if (!string.IsNullOrWhiteSpace(label)) return label!;

        var basis = _invoice.Items.FirstOrDefault()?.TradeDetails?.RateBasis
                    ?? TradeRateBasis.PerQuantityUnit;
        var stem = basis == TradeRateBasis.PerNetWeight ? "Rate/Kg" : "Rate";
        return Join(stem, _details.PricingTerm, _currency);
    }

    private string AmountHeading() => Join("Total Amount", _details.PricingTerm, _currency);

    // ---- totals ----------------------------------------------------------

    private void ComposeTotalsBlock(IContainer container)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(7);
                columns.RelativeColumn(24);
                columns.RelativeColumn(10);
                columns.RelativeColumn(10);
                columns.RelativeColumn(9);
                columns.RelativeColumn(9);
                columns.RelativeColumn(9);
                columns.RelativeColumn(11);
                columns.RelativeColumn(13);
            });

            // The reference restates the three shipment totals as their own labelled rows before
            // the grand total. Kept, because it is what the reader is scanning for.
            SummaryRow(table, $"NET WT IN {_details.WeightUnit}", Weight(_details.TotalNetWeight));
            SummaryRow(table, $"GROSS WT IN {_details.WeightUnit}", Weight(_details.TotalGrossWeight));
            SummaryRow(table, PackageLabel(), CurrencyFormatter.Quantity(_details.TotalPackages));

            table.Cell().ColumnSpan(1).Element(TotalBox).AlignLeft().Text("TOTAL").Bold();
            table.Cell().ColumnSpan(3).Element(TotalBox).Text(string.Empty);
            table.Cell().Element(TotalBox).AlignRight().Text(Weight(_details.TotalNetWeight)).Bold();
            table.Cell().Element(TotalBox).AlignRight().Text(Weight(_details.TotalGrossWeight)).Bold();
            table.Cell().Element(TotalBox).AlignRight()
                .Text(CurrencyFormatter.Quantity(_details.TotalPackages)).Bold();
            table.Cell().Element(TotalBox).AlignRight().Text(_currency).Bold();
            table.Cell().Element(TotalBox).AlignRight().Text(Money(_invoice.GrandTotal)).Bold();
        });
    }

    /// <summary>
    /// Whether tax and discount rows appear at all.
    ///
    /// An export proforma normally shows neither, and printing "Discount 0.00 / Tax 0.00" on a
    /// customs document adds two lines that mean nothing. They appear only when there is something
    /// to report — at which point leaving them out would make the grand total look wrong.
    /// </summary>
    private bool HasAdjustments => _invoice.DiscountTotal != 0m || _invoice.TaxTotal != 0m;

    private void SummaryRow(TableDescriptor table, string label, string value)
    {
        table.Cell().ColumnSpan(1).Element(SummaryBox).Text(string.Empty);
        table.Cell().ColumnSpan(1).Element(SummaryBox).AlignRight().Text(label).SemiBold();
        table.Cell().ColumnSpan(1).Element(SummaryBox).AlignRight().Text(value).SemiBold();
        table.Cell().ColumnSpan(6).Element(SummaryBox).Text(string.Empty);
    }

    private string PackageLabel()
    {
        // Named after what the goods are actually packed in, when every line agrees; "PACKAGES"
        // when they do not, rather than labelling bags as boxes.
        var units = _invoice.Items
            .Select(i => i.TradeDetails?.QuantityUnit ?? i.Unit)
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u!.Trim().ToUpperInvariant())
            .Distinct().ToList();

        return units.Count == 1 ? units[0] : "PACKAGES";
    }

    private void ComposeAmountInWords(IContainer container)
    {
        container.Column(column =>
        {
            if (HasAdjustments)
            {
                column.Item().BorderHorizontal(Border).BorderVertical(Border).BorderColor(Rule)
                    .PaddingVertical(3).PaddingHorizontal(6).Row(row =>
                {
                    row.RelativeItem().Text(string.Empty);
                    row.ConstantItem(200).Column(c =>
                    {
                        AdjustmentLine(c, "Subtotal", _invoice.Subtotal);
                        if (_invoice.DiscountTotal != 0m)
                            AdjustmentLine(c, "Discount", -_invoice.DiscountTotal);
                        if (_invoice.TaxTotal != 0m)
                            AdjustmentLine(c, "Tax", _invoice.TaxTotal);
                    });
                });
            }

            column.Item().BorderBottom(Border).BorderVertical(Border).BorderColor(Rule)
                .PaddingVertical(4).PaddingHorizontal(6).Text(text =>
            {
                text.Span("Amount chargeable (in words): ").FontSize(7).FontColor(Muted);
                text.Span(AmountInWords.Format(_invoice.GrandTotal, _currency)).Bold();
            });
        });
    }

    private void AdjustmentLine(ColumnDescriptor column, string label, decimal value)
    {
        column.Item().Row(row =>
        {
            row.RelativeItem().Text(label).FontSize(7).FontColor(Muted);
            row.ConstantItem(90).AlignRight().Text(Money(value));
        });
    }

    // ---- declaration + signature -----------------------------------------

    private void ComposeDeclarationBlock(IContainer container)
    {
        container.BorderBottom(Border).BorderVertical(Border).BorderColor(Rule).Row(row =>
        {
            row.RelativeItem(60).Padding(6).Column(column =>
            {
                var declaration = string.IsNullOrWhiteSpace(_details.FooterDeclaration)
                    ? null
                    : _details.FooterDeclaration!.Trim();

                if (declaration is not null)
                {
                    column.Item().Text(text =>
                    {
                        text.Span("Declaration: ").SemiBold();
                        text.Span(declaration);
                    });
                }

                if (!string.IsNullOrWhiteSpace(_invoice.Notes))
                    column.Item().PaddingTop(4).Text(_invoice.Notes!).FontSize(7).FontColor(Muted);
            });

            row.RelativeItem(40).BorderLeft(Border).BorderColor(Rule).Padding(6).Column(column =>
            {
                column.Item().AlignRight().Text($"For {SignatoryOrganisation()}").SemiBold();
                // Deliberate empty space: this document is signed by hand. Quotely does not draw a
                // signature it was not given, and a printed name in a signature box would read as
                // one to anyone glancing at it.
                column.Item().Height(34);
                column.Item().AlignRight().Text("Authorised Signatory").FontSize(7).FontColor(Muted);
            });
        });
    }

    private string SignatoryOrganisation() =>
        !string.IsNullOrWhiteSpace(_details.AuthorisedSignatory) ? _details.AuthorisedSignatory!.Trim()
        : !string.IsNullOrWhiteSpace(_details.PartyName) ? _details.PartyName.Trim()
        : _business?.BusinessName ?? string.Empty;

    private void ComposeFooter(IContainer container)
    {
        container.PaddingTop(6).Row(row =>
        {
            row.RelativeItem().Text(text =>
            {
                text.DefaultTextStyle(t => t.FontSize(7).FontColor(Muted));
                text.Span($"{Title} {DocumentNumber}");
            });

            row.RelativeItem().AlignRight().Text(text =>
            {
                text.DefaultTextStyle(t => t.FontSize(7).FontColor(Muted));
                text.Span("Page ");
                text.CurrentPageNumber();
                text.Span(" of ");
                text.TotalPages();
            });
        });
    }

    // ---- cells ------------------------------------------------------------

    private void AddressCell(IContainer container, string label, string? name, string? address, bool logo = false)
    {
        container.Padding(5).Column(column =>
        {
            column.Spacing(1.5f);

            if (logo && _logo is not null)
                column.Item().PaddingBottom(3).Height(26).AlignLeft().Image(_logo).FitHeight();

            column.Item().Text(label).FontSize(7).FontColor(Muted);

            if (!string.IsNullOrWhiteSpace(name))
                column.Item().Text(name!).Bold();

            if (!string.IsNullOrWhiteSpace(address))
                column.Item().Text(address!).LineHeight(1.25f);
        });
    }

    /// <summary>A label above its value, which is how the reference boxes read.</summary>
    private void LabelledCell(IContainer container, string label, string? value, bool strong = false)
    {
        container.Padding(5).Column(column =>
        {
            column.Spacing(1.5f);
            column.Item().Text(label).FontSize(7).FontColor(Muted);
            // A non-breaking space keeps an empty box the same height as a filled one, so the grid
            // does not buckle when a shipment has no vessel number yet.
            var text = column.Item().Text(string.IsNullOrWhiteSpace(value) ? "\u00A0" : value!);
            if (strong) text.SemiBold();
        });
    }

    /// <summary>A label and its value on one line, for the narrower rows.</summary>
    private void InlineCell(IContainer container, string label, string? value)
    {
        container.Padding(5).Text(text =>
        {
            text.Span($"{label} ").FontSize(7).FontColor(Muted);
            text.Span(string.IsNullOrWhiteSpace(value) ? " " : value!).SemiBold();
        });
    }

    private static void HeaderCell(IContainer container, string text,
        HorizontalAlignment align = HorizontalAlignment.Center)
    {
        var cell = container.Border(Border).BorderColor(Rule).Background(HeaderFill)
            .PaddingVertical(3).PaddingHorizontal(3);

        cell = align switch
        {
            HorizontalAlignment.Left => cell.AlignLeft(),
            HorizontalAlignment.Right => cell.AlignRight(),
            _ => cell.AlignCenter(),
        };

        cell.Text(text).FontSize(7).Bold();
    }

    private static void BodyCell(IContainer container, string? text,
        HorizontalAlignment align = HorizontalAlignment.Center)
    {
        var cell = container.Element(BodyBox);

        cell = align switch
        {
            HorizontalAlignment.Left => cell.AlignLeft(),
            HorizontalAlignment.Right => cell.AlignRight(),
            _ => cell.AlignCenter(),
        };

        cell.Text(string.IsNullOrWhiteSpace(text) ? string.Empty : text!);
    }

    private static IContainer BodyBox(IContainer container) =>
        container.Border(Border).BorderColor(Rule)
            // ShowEntire keeps a goods line whole: half a consignment line at a page break, with
            // its weights on one page and its amount on the next, is a document nobody can check.
            .ShowEntire()
            .PaddingVertical(3).PaddingHorizontal(3);

    private static IContainer SummaryBox(IContainer container) =>
        container.BorderLeft(Border).BorderRight(Border).BorderColor(Rule)
            .PaddingVertical(2).PaddingHorizontal(3);

    private static IContainer TotalBox(IContainer container) =>
        container.Border(Border).BorderColor(Rule).Background(HeaderFill)
            .PaddingVertical(3.5f).PaddingHorizontal(3);

    // ---- formatting -------------------------------------------------------

    private string Money(decimal value) =>
        value.ToString("#,##0.00", CultureInfo.InvariantCulture);

    private static string Weight(decimal? value) =>
        value is null or 0m ? string.Empty : value.Value.ToString("#,##0.00", CultureInfo.InvariantCulture);

    private static string QuantityText(InvoiceItem item) => CurrencyFormatter.Quantity(item.Quantity);

    private static string FormatDate(DateOnly date) =>
        date.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);

    private string BuyerOrderText()
    {
        var parts = new[]
        {
            _details.BuyerOrderNumber,
            _details.BuyerOrderDate is { } d ? FormatDate(d) : null,
        }.Where(p => !string.IsNullOrWhiteSpace(p));

        return string.Join("  ·  ", parts);
    }

    private string TermsText()
    {
        var parts = new[] { _details.TermsOfDelivery, _details.TermsOfPayment }
            .Where(p => !string.IsNullOrWhiteSpace(p));
        return string.Join("  ·  ", parts);
    }

    private string ApedaText()
    {
        var text = _details.ApedaRegistrationNumber?.Trim() ?? string.Empty;
        return _details.ApedaValidUntil is { } until
            ? $"{text} valid upto {FormatDate(until)}"
            : text;
    }

    private static string Join(params string?[] parts) =>
        string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));

    private static IEnumerable<string> SplitLines(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
