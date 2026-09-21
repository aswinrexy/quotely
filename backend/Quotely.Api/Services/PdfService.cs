using System.Text.RegularExpressions;
using QuestPDF.Fluent;
using Quotely.Api.Middleware;
using Quotely.Api.Models;
using Quotely.Api.Pdf;

namespace Quotely.Api.Services;

public record GeneratedPdf(byte[] Content, string FileName);

public interface IPdfService
{
    GeneratedPdf Generate(Quotation quotation, BusinessProfile? business);
    GeneratedPdf GenerateInvoice(Invoice invoice, BusinessProfile? business);

    /// <summary>
    /// The import/export layout. A separate entry point rather than a branch inside
    /// GenerateInvoice, so a domestic invoice can never be routed through the trade document by
    /// accident and the existing method keeps behaving exactly as it did.
    /// </summary>
    GeneratedPdf GenerateTradeInvoice(Invoice invoice, BusinessProfile? business);
}

public class PdfService : IPdfService
{
    private readonly ILogger<PdfService> _logger;

    /// <summary>
    /// QuestPDF refuses to render until a license is declared. Program.cs sets it at startup, but
    /// declaring it here too keeps the service usable on its own — in unit tests, for instance.
    /// </summary>
    static PdfService() => QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

    public PdfService(ILogger<PdfService> logger) => _logger = logger;

    public GeneratedPdf Generate(Quotation quotation, BusinessProfile? business)
    {
        try
        {
            var document = new QuotationDocument(quotation, business, DecodeLogo(business?.LogoUrl));
            return new GeneratedPdf(document.GeneratePdf(), BuildFileName(quotation));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PDF generation failed for quotation {QuotationId}", quotation.Id);
            throw new ApiException(System.Net.HttpStatusCode.InternalServerError,
                "The quotation PDF could not be generated. Please try again.");
        }
    }

    public GeneratedPdf GenerateInvoice(Invoice invoice, BusinessProfile? business)
    {
        try
        {
            var document = new InvoiceDocument(invoice, business, DecodeLogo(business?.LogoUrl));
            return new GeneratedPdf(document.GeneratePdf(), BuildFileName(invoice));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PDF generation failed for invoice {InvoiceId}", invoice.Id);
            throw new ApiException(System.Net.HttpStatusCode.InternalServerError,
                "The invoice PDF could not be generated. Please try again.");
        }
    }

    public GeneratedPdf GenerateTradeInvoice(Invoice invoice, BusinessProfile? business)
    {
        try
        {
            var document = new TradeInvoiceDocument(invoice, business, DecodeLogo(business?.LogoUrl));
            return new GeneratedPdf(document.GeneratePdf(), BuildTradeFileName(invoice));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PDF generation failed for trade invoice {InvoiceId}", invoice.Id);
            throw new ApiException(System.Net.HttpStatusCode.InternalServerError,
                "The import/export invoice PDF could not be generated. Please try again.");
        }
    }

    /// <summary>
    /// Example: PI-NAFPCL-103-26-27-Root-Express.pdf
    ///
    /// Named after the exporter's own document number when they keep one, because that is the
    /// reference their buyer, bank and forwarder will quote back at them — not Quotely's INV-.
    /// </summary>
    public static string BuildTradeFileName(Invoice invoice)
    {
        var details = invoice.TradeDetails;
        var prefix = details?.DocumentType == Models.TradeDocumentType.CommercialInvoice ? "CI" : "PI";

        var number = Slug(string.IsNullOrWhiteSpace(details?.DocumentNumber)
            ? invoice.InvoiceNumber
            : details!.DocumentNumber!);

        var party = Slug(details?.ConsigneeName ?? invoice.CustomerName);

        return string.IsNullOrEmpty(party)
            ? $"{prefix}-{number}.pdf"
            : $"{prefix}-{number}-{party}.pdf";
    }

    /// <summary>Example: INV-000001-John-Smith.pdf</summary>
    public static string BuildFileName(Invoice invoice)
    {
        // The snapshotted name, so the file matches the document even if the customer was renamed.
        var customer = Slug(invoice.CustomerName);
        return string.IsNullOrEmpty(customer)
            ? $"{invoice.InvoiceNumber}.pdf"
            : $"{invoice.InvoiceNumber}-{customer}.pdf";
    }

    /// <summary>Example: QT-000001-John-Smith.pdf</summary>
    public static string BuildFileName(Quotation quotation)
    {
        var customer = Slug(quotation.Customer?.Name);
        return string.IsNullOrEmpty(customer)
            ? $"{quotation.QuotationNumber}.pdf"
            : $"{quotation.QuotationNumber}-{customer}.pdf";
    }

    private static string Slug(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var cleaned = Regex.Replace(value, @"[^A-Za-z0-9]+", "-").Trim('-');
        return cleaned.Length > 40 ? cleaned[..40].Trim('-') : cleaned;
    }

    /// <summary>Logos are stored as data URIs in V1; anything else is ignored rather than fetched.</summary>
    private static byte[]? DecodeLogo(string? logoUrl)
    {
        if (string.IsNullOrWhiteSpace(logoUrl)) return null;

        var match = Regex.Match(logoUrl, @"^data:image/(png|jpe?g);base64,(?<data>.+)$", RegexOptions.Singleline);
        if (!match.Success) return null;

        try
        {
            return Convert.FromBase64String(match.Groups["data"].Value);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
