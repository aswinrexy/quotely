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
}

public class PdfService : IPdfService
{
    private readonly ILogger<PdfService> _logger;

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
