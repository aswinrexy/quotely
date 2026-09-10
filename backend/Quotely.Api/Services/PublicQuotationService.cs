using System.Net;
using Microsoft.EntityFrameworkCore;
using Quotely.Api.Data;
using Quotely.Api.DTOs;
using Quotely.Api.Middleware;
using Quotely.Api.Models;

namespace Quotely.Api.Services;

public class PublicLinkOptions
{
    public const string SectionName = "PublicLinks";

    /// <summary>Origin of the customer-facing site, used to build the shareable URL.</summary>
    public string BaseUrl { get; set; } = "http://localhost:3000";
}

public interface IPublicQuotationService
{
    Task<PublicQuotationLinkDto> CreateLinkAsync(Guid userId, Guid quotationId, CancellationToken ct = default);
    Task<PublicQuotationDto> GetAsync(string token, CancellationToken ct = default);
    Task<PublicQuotationDto> RespondAsync(string token, QuotationStatus decision, PublicResponseRequest request, CancellationToken ct = default);
    Task<(Quotation Quotation, BusinessProfile? Business)> GetForPdfAsync(string token, CancellationToken ct = default);
}

public class PublicQuotationService : IPublicQuotationService
{
    private readonly AppDbContext _db;
    private readonly PublicLinkOptions _options;

    public PublicQuotationService(AppDbContext db, Microsoft.Extensions.Options.IOptions<PublicLinkOptions> options)
    {
        _db = db;
        _options = options.Value;
    }

    /// <summary>
    /// Creates (or replaces) the share link for one of the caller's own quotations.
    /// Because only the hash is stored, the raw token cannot be recovered later — asking for a
    /// link again mints a new one and the previous URL stops working. That doubles as revocation.
    /// </summary>
    public async Task<PublicQuotationLinkDto> CreateLinkAsync(Guid userId, Guid quotationId, CancellationToken ct = default)
    {
        // Ownership is enforced here, exactly as on every other authenticated quotation query.
        var quotation = await _db.Quotations.FirstOrDefaultAsync(q => q.Id == quotationId && q.UserId == userId, ct)
                        ?? throw ApiException.NotFound("Quotation");

        var token = PublicTokenGenerator.CreateToken();
        quotation.PublicTokenHash = PublicTokenGenerator.Hash(token);
        quotation.PublicLinkCreatedAt = DateTime.UtcNow;

        // Sharing the link is the act of sending the quotation, so a draft becomes Sent.
        // Any other status (including a quotation already accepted or rejected) is left alone.
        if (quotation.Status == QuotationStatus.Draft)
            quotation.Status = QuotationStatus.Sent;

        await _db.SaveChangesAsync(ct);

        return new PublicQuotationLinkDto(BuildUrl(token), quotation.PublicLinkCreatedAt.Value);
    }

    public async Task<PublicQuotationDto> GetAsync(string token, CancellationToken ct = default)
    {
        var quotation = await FindByTokenAsync(token, tracking: false, ct);
        var business = await LoadBusinessAsync(quotation.UserId, ct);
        return Map(quotation, business);
    }

    public async Task<PublicQuotationDto> RespondAsync(
        string token, QuotationStatus decision, PublicResponseRequest request, CancellationToken ct = default)
    {
        if (decision is not (QuotationStatus.Accepted or QuotationStatus.Rejected))
            throw ApiException.BadRequest("Unsupported response.");

        if (string.IsNullOrWhiteSpace(request.Name))
            throw ApiException.BadRequest("Please enter your name.");

        var quotation = await FindByTokenAsync(token, tracking: true, ct);

        // A quotation may be answered once. Re-posting never silently flips a decision.
        if (quotation.HasResponded)
            throw ApiException.Conflict(
                $"This quotation was already {quotation.Status.ToString().ToLowerInvariant()} and cannot be changed here.");

        if (quotation.IsExpired(Today))
            throw ApiException.Conflict(
                "This quotation has expired. Please contact the business for an updated quotation.");

        // The customer supplies who they are and what they said — never the status or any amount.
        quotation.Status = decision;
        quotation.RespondedAt = DateTime.UtcNow;
        quotation.RespondedByName = request.Name.Trim();
        quotation.RespondedByEmail = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        quotation.ResponseComment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();

        await _db.SaveChangesAsync(ct);

        var business = await LoadBusinessAsync(quotation.UserId, ct);
        return Map(quotation, business);
    }

    public async Task<(Quotation Quotation, BusinessProfile? Business)> GetForPdfAsync(string token, CancellationToken ct = default)
    {
        var quotation = await FindByTokenAsync(token, tracking: false, ct);
        return (quotation, await LoadBusinessAsync(quotation.UserId, ct));
    }

    // ---- helpers -------------------------------------------------------

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>
    /// The hash is the only lookup key, so a token can never resolve to a different quotation.
    /// Anything unknown is a plain 404 that reveals nothing about which quotations exist.
    /// </summary>
    private async Task<Quotation> FindByTokenAsync(string token, bool tracking, CancellationToken ct)
    {
        if (!PublicTokenGenerator.LooksValid(token))
            throw ApiException.NotFound("Quotation");

        var hash = PublicTokenGenerator.Hash(token);

        var query = _db.Quotations
            .Include(q => q.Customer)
            .Include(q => q.Items.OrderBy(i => i.SortOrder))
            .Where(q => q.PublicTokenHash == hash);

        if (!tracking) query = query.AsNoTracking();

        return await query.FirstOrDefaultAsync(ct) ?? throw ApiException.NotFound("Quotation");
    }

    private Task<BusinessProfile?> LoadBusinessAsync(Guid userId, CancellationToken ct) =>
        _db.BusinessProfiles.AsNoTracking().FirstOrDefaultAsync(b => b.UserId == userId, ct);

    private string BuildUrl(string token) => $"{_options.BaseUrl.TrimEnd('/')}/q/{token}";

    private static PublicQuotationDto Map(Quotation q, BusinessProfile? business)
    {
        var expired = q.IsExpired(Today);

        return new PublicQuotationDto
        {
            QuotationNumber = q.QuotationNumber,
            QuotationDate = q.QuotationDate,
            ValidUntil = q.ValidUntil,
            Business = new PublicBusinessDto
            {
                BusinessName = business?.BusinessName ?? "Quotation",
                Email = business?.BusinessEmail,
                Phone = business?.Phone,
                AddressLine = business?.AddressLine,
                City = business?.City,
                State = business?.State,
                PostalCode = business?.PostalCode,
                Country = business?.Country,
                TaxNumber = business?.TaxNumber,
                LogoUrl = business?.LogoUrl
            },
            Customer = new PublicCustomerDto
            {
                Name = q.Customer?.Name ?? string.Empty,
                CompanyName = q.Customer?.CompanyName,
                Email = q.Customer?.Email,
                Phone = q.Customer?.Phone,
                AddressLine = q.Customer?.AddressLine,
                City = q.Customer?.City,
                State = q.Customer?.State,
                PostalCode = q.Customer?.PostalCode,
                Country = q.Customer?.Country
            },
            Items = q.Items.OrderBy(i => i.SortOrder).Select(i => new PublicQuotationItemDto
            {
                Name = i.Name,
                Description = i.Description,
                Unit = i.Unit,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice,
                Discount = i.Discount,
                TaxRate = i.TaxRate,
                LineTotal = i.LineTotal
            }).ToList(),
            // Totals come straight from the stored, server-calculated values.
            Subtotal = q.Subtotal,
            DiscountTotal = q.DiscountTotal,
            TaxTotal = q.TaxTotal,
            GrandTotal = q.GrandTotal,
            Currency = business?.Currency ?? "INR",
            Notes = q.Notes,
            Terms = q.Terms,
            Status = q.Status.ToString(),
            IsExpired = expired,
            CanRespond = !q.HasResponded && !expired,
            RespondedAt = q.RespondedAt,
            RespondedByName = q.RespondedByName
        };
    }
}
