using Microsoft.EntityFrameworkCore;
using Quotely.Api.Data;
using Quotely.Api.DTOs;
using Quotely.Api.Middleware;
using Quotely.Api.Models;

namespace Quotely.Api.Services;

public interface IQuotationService
{
    Task<PagedResult<QuotationListItemDto>> ListAsync(Guid userId, string? search, string? status, int page, int pageSize, CancellationToken ct = default);
    Task<QuotationDto> GetAsync(Guid userId, Guid id, CancellationToken ct = default);
    Task<QuotationDto> CreateAsync(Guid userId, SaveQuotationRequest request, CancellationToken ct = default);
    Task<QuotationDto> UpdateAsync(Guid userId, Guid id, SaveQuotationRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid userId, Guid id, CancellationToken ct = default);
    Task<DashboardStatsDto> GetDashboardAsync(Guid userId, CancellationToken ct = default);
    Task<Quotation> GetEntityForPdfAsync(Guid userId, Guid id, CancellationToken ct = default);
}

public class QuotationService : IQuotationService
{
    private readonly AppDbContext _db;

    public QuotationService(AppDbContext db) => _db = db;

    public async Task<PagedResult<QuotationListItemDto>> ListAsync(
        Guid userId, string? search, string? status, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        // Every query starts from the authenticated user's rows.
        var query = _db.Quotations.AsNoTracking().Where(q => q.UserId == userId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(q =>
                q.QuotationNumber.Contains(term) ||
                q.Customer!.Name.Contains(term) ||
                q.Customer.CompanyName!.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<QuotationStatus>(status, true, out var parsed))
            query = query.Where(q => q.Status == parsed);

        var total = await query.CountAsync(ct);
        var currency = await GetCurrencyAsync(userId, ct);

        var items = await query
            .OrderByDescending(q => q.Sequence)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(q => new QuotationListItemDto
            {
                Id = q.Id,
                QuotationNumber = q.QuotationNumber,
                CustomerId = q.CustomerId,
                CustomerName = q.Customer!.Name,
                QuotationDate = q.QuotationDate,
                ValidUntil = q.ValidUntil,
                Status = q.Status.ToString(),
                GrandTotal = q.GrandTotal,
                Currency = currency,
                RespondedAt = q.RespondedAt
            })
            .ToListAsync(ct);

        return new PagedResult<QuotationListItemDto>(items, page, pageSize, total);
    }

    public async Task<QuotationDto> GetAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var quotation = await LoadAsync(userId, id, tracking: false, ct);
        var business = await _db.BusinessProfiles.AsNoTracking().FirstOrDefaultAsync(b => b.UserId == userId, ct);
        return Map(quotation, business);
    }

    public async Task<Quotation> GetEntityForPdfAsync(Guid userId, Guid id, CancellationToken ct = default)
        => await LoadAsync(userId, id, tracking: false, ct);

    public async Task<QuotationDto> CreateAsync(Guid userId, SaveQuotationRequest request, CancellationToken ct = default)
    {
        Validate(request);
        await EnsureCustomerOwnedAsync(userId, request.CustomerId, ct);

        var sequence = await NextSequenceAsync(userId, ct);
        var quotation = new Quotation
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CustomerId = request.CustomerId,
            Sequence = sequence,
            QuotationNumber = FormatNumber(sequence),
            QuotationDate = request.QuotationDate,
            ValidUntil = request.ValidUntil,
            Notes = request.Notes,
            Terms = request.Terms,
            Status = ParseStatus(request.Status)
        };

        foreach (var item in await BuildItemsAsync(userId, quotation.Id, request.Items, ct))
            quotation.Items.Add(item);
        QuotationCalculator.ApplyTotals(quotation);

        _db.Quotations.Add(quotation);
        await _db.SaveChangesAsync(ct);

        return await GetAsync(userId, quotation.Id, ct);
    }

    public async Task<QuotationDto> UpdateAsync(Guid userId, Guid id, SaveQuotationRequest request, CancellationToken ct = default)
    {
        Validate(request);
        // Loaded without items so the rebuilt lines below can't collide with tracked originals.
        var quotation = await _db.Quotations.FirstOrDefaultAsync(q => q.Id == id && q.UserId == userId, ct)
                        ?? throw ApiException.NotFound("Quotation");
        await EnsureCustomerOwnedAsync(userId, request.CustomerId, ct);

        quotation.CustomerId = request.CustomerId;
        quotation.QuotationDate = request.QuotationDate;
        quotation.ValidUntil = request.ValidUntil;
        quotation.Notes = request.Notes;
        quotation.Terms = request.Terms;
        quotation.Status = ParseStatus(request.Status, quotation.Status);
        // The share link and any recorded customer response are deliberately untouched here:
        // editing a quotation must not silently revoke a link the customer already has.

        // An edit replaces the whole line set: simpler and safer than diffing rows.
        await _db.QuotationItems.Where(i => i.QuotationId == id).ExecuteDeleteAsync(ct);

        var items = await BuildItemsAsync(userId, quotation.Id, request.Items, ct);
        QuotationCalculator.ApplyTotals(quotation, items);
        _db.QuotationItems.AddRange(items);

        await _db.SaveChangesAsync(ct);
        return await GetAsync(userId, id, ct);
    }

    public async Task DeleteAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var quotation = await LoadAsync(userId, id, tracking: true, ct);
        _db.Quotations.Remove(quotation);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<DashboardStatsDto> GetDashboardAsync(Guid userId, CancellationToken ct = default)
    {
        var rows = await _db.Quotations.AsNoTracking()
            .Where(q => q.UserId == userId)
            .Select(q => new { q.Status, q.GrandTotal })
            .ToListAsync(ct);

        return new DashboardStatsDto(
            TotalQuotations: rows.Count,
            DraftCount: rows.Count(r => r.Status == QuotationStatus.Draft),
            SentCount: rows.Count(r => r.Status == QuotationStatus.Sent),
            AcceptedCount: rows.Count(r => r.Status == QuotationStatus.Accepted),
            TotalValue: rows.Sum(r => r.GrandTotal),
            Currency: await GetCurrencyAsync(userId, ct));
    }

    // ---- helpers -------------------------------------------------------

    private async Task<Quotation> LoadAsync(Guid userId, Guid id, bool tracking, CancellationToken ct)
    {
        var query = _db.Quotations
            .Include(q => q.Customer)
            .Include(q => q.Items.OrderBy(i => i.SortOrder))
            .Where(q => q.Id == id && q.UserId == userId);

        if (!tracking) query = query.AsNoTracking();

        // A quotation belonging to another user is reported as 404, not 403, so ids can't be probed.
        return await query.FirstOrDefaultAsync(ct) ?? throw ApiException.NotFound("Quotation");
    }

    private async Task EnsureCustomerOwnedAsync(Guid userId, Guid customerId, CancellationToken ct)
    {
        var owned = await _db.Customers.AsNoTracking().AnyAsync(c => c.Id == customerId && c.UserId == userId, ct);
        if (!owned) throw ApiException.BadRequest("Select a valid customer.");
    }

    private async Task<List<QuotationItem>> BuildItemsAsync(
        Guid userId, Guid quotationId, List<SaveQuotationItemRequest> items, CancellationToken ct)
    {
        // Only the user's own products may be referenced; anything else is stored as a free-text line.
        var requestedProductIds = items.Where(i => i.ProductId.HasValue).Select(i => i.ProductId!.Value).Distinct().ToList();
        var ownedProductIds = requestedProductIds.Count == 0
            ? new HashSet<Guid>()
            : (await _db.Products.AsNoTracking()
                .Where(p => p.UserId == userId && requestedProductIds.Contains(p.Id))
                .Select(p => p.Id).ToListAsync(ct)).ToHashSet();

        var order = 0;
        var built = new List<QuotationItem>(items.Count);
        foreach (var item in items)
        {
            built.Add(new QuotationItem
            {
                Id = Guid.NewGuid(),
                QuotationId = quotationId,
                ProductId = item.ProductId.HasValue && ownedProductIds.Contains(item.ProductId.Value) ? item.ProductId : null,
                SortOrder = order++,
                Name = item.Name.Trim(),
                Description = item.Description,
                Unit = string.IsNullOrWhiteSpace(item.Unit) ? "Service" : item.Unit.Trim(),
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                Discount = item.Discount,
                TaxRate = item.TaxRate
            });
        }

        return built;
    }

    private static void Validate(SaveQuotationRequest request)
    {
        if (request.Items is null || request.Items.Count == 0)
            throw ApiException.BadRequest("Add at least one item to the quotation.");

        if (request.ValidUntil < request.QuotationDate)
            throw ApiException.BadRequest("Valid until must be on or after the quotation date.");

        foreach (var item in request.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Name))
                throw ApiException.BadRequest("Every item needs a description.");
            if (item.Quantity <= 0)
                throw ApiException.BadRequest($"Quantity for \"{item.Name}\" must be greater than zero.");
            if (item.UnitPrice < 0)
                throw ApiException.BadRequest($"Unit price for \"{item.Name}\" cannot be negative.");
            if (item.Discount < 0)
                throw ApiException.BadRequest($"Discount for \"{item.Name}\" cannot be negative.");
            if (item.TaxRate is < 0 or > 100)
                throw ApiException.BadRequest($"Tax rate for \"{item.Name}\" must be between 0 and 100.");
        }
    }

    private static QuotationStatus ParseStatus(string? status, QuotationStatus fallback = QuotationStatus.Draft)
    {
        if (string.IsNullOrWhiteSpace(status)) return fallback;
        if (!Enum.TryParse<QuotationStatus>(status, true, out var parsed))
            throw ApiException.BadRequest("Unknown quotation status.");
        return parsed;
    }

    /// <summary>Quotation numbers are sequential per user, so two businesses both start at QT-000001.</summary>
    private async Task<int> NextSequenceAsync(Guid userId, CancellationToken ct)
    {
        var last = await _db.Quotations.AsNoTracking()
            .Where(q => q.UserId == userId)
            .OrderByDescending(q => q.Sequence)
            .Select(q => (int?)q.Sequence)
            .FirstOrDefaultAsync(ct);
        return (last ?? 0) + 1;
    }

    public static string FormatNumber(int sequence) => $"QT-{sequence:D6}";

    private async Task<string> GetCurrencyAsync(Guid userId, CancellationToken ct) =>
        await _db.BusinessProfiles.AsNoTracking()
            .Where(b => b.UserId == userId)
            .Select(b => b.Currency)
            .FirstOrDefaultAsync(ct) ?? "INR";

    public static QuotationDto Map(Quotation q, BusinessProfile? business) => new()
    {
        Id = q.Id,
        QuotationNumber = q.QuotationNumber,
        Customer = new CustomerDto
        {
            Id = q.Customer!.Id,
            Name = q.Customer.Name,
            CompanyName = q.Customer.CompanyName,
            Email = q.Customer.Email,
            Phone = q.Customer.Phone,
            AddressLine = q.Customer.AddressLine,
            City = q.Customer.City,
            State = q.Customer.State,
            PostalCode = q.Customer.PostalCode,
            Country = q.Customer.Country,
            Notes = q.Customer.Notes,
            CreatedAt = q.Customer.CreatedAt
        },
        Business = business is null ? null : new BusinessProfileDto
        {
            Id = business.Id,
            BusinessName = business.BusinessName,
            BusinessEmail = business.BusinessEmail,
            Phone = business.Phone,
            AddressLine = business.AddressLine,
            City = business.City,
            State = business.State,
            PostalCode = business.PostalCode,
            Country = business.Country,
            TaxNumber = business.TaxNumber,
            LogoUrl = business.LogoUrl,
            Currency = business.Currency
        },
        QuotationDate = q.QuotationDate,
        ValidUntil = q.ValidUntil,
        Notes = q.Notes,
        Terms = q.Terms,
        Status = q.Status.ToString(),
        Subtotal = q.Subtotal,
        DiscountTotal = q.DiscountTotal,
        TaxTotal = q.TaxTotal,
        GrandTotal = q.GrandTotal,
        Currency = business?.Currency ?? "INR",
        Items = q.Items.OrderBy(i => i.SortOrder).Select(i => new QuotationItemDto
        {
            Id = i.Id,
            ProductId = i.ProductId,
            Name = i.Name,
            Description = i.Description,
            Unit = i.Unit,
            Quantity = i.Quantity,
            UnitPrice = i.UnitPrice,
            Discount = i.Discount,
            TaxRate = i.TaxRate,
            LineSubtotal = i.LineSubtotal,
            LineTax = i.LineTax,
            LineTotal = i.LineTotal
        }).ToList(),
        HasPublicLink = q.PublicTokenHash is not null,
        PublicLinkCreatedAt = q.PublicLinkCreatedAt,
        RespondedAt = q.RespondedAt,
        RespondedByName = q.RespondedByName,
        RespondedByEmail = q.RespondedByEmail,
        ResponseComment = q.ResponseComment,
        CreatedAt = q.CreatedAt,
        UpdatedAt = q.UpdatedAt
    };
}
