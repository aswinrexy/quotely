using Microsoft.EntityFrameworkCore;
using Quotely.Api.Data;
using Quotely.Api.DTOs;
using Quotely.Api.Middleware;
using Quotely.Api.Models;

namespace Quotely.Api.Services;

public interface IInvoiceService
{
    Task<PagedResult<InvoiceListItemDto>> ListAsync(Guid userId, string? search, string? status, int page, int pageSize, CancellationToken ct = default);
    Task<InvoiceDto> GetAsync(Guid userId, Guid id, CancellationToken ct = default);
    Task<InvoiceDto> ConvertFromQuotationAsync(Guid userId, Guid quotationId, CancellationToken ct = default);
    Task<InvoiceDto> UpdateAsync(Guid userId, Guid id, SaveInvoiceRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid userId, Guid id, CancellationToken ct = default);
    Task<Invoice> GetEntityForPdfAsync(Guid userId, Guid id, CancellationToken ct = default);
}

public class InvoiceService : IInvoiceService
{
    /// <summary>Default payment window. There is no payment-terms setting in the product yet.</summary>
    public const int DefaultPaymentTermDays = 15;

    private readonly AppDbContext _db;

    public InvoiceService(AppDbContext db) => _db = db;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    // ---- conversion ----------------------------------------------------

    /// <summary>
    /// Raises the invoice for an accepted quotation. Items and billing details are copied, not
    /// referenced, and the totals are recomputed from those copies — the catalogue is not consulted.
    /// </summary>
    public async Task<InvoiceDto> ConvertFromQuotationAsync(Guid userId, Guid quotationId, CancellationToken ct = default)
    {
        // Ownership first: another user's quotation is a 404, never a hint that it exists.
        var quotation = await _db.Quotations
            .Include(q => q.Customer)
            .Include(q => q.Items.OrderBy(i => i.SortOrder))
            .FirstOrDefaultAsync(q => q.Id == quotationId && q.UserId == userId, ct)
            ?? throw ApiException.NotFound("Quotation");

        if (quotation.Status != QuotationStatus.Accepted)
            throw ApiException.Conflict(
                $"Only an accepted quotation can be invoiced. {quotation.QuotationNumber} is {quotation.Status}.");

        if (quotation.Items.Count == 0)
            throw ApiException.BadRequest("This quotation has no items to invoice.");

        var existing = await _db.Invoices.AsNoTracking()
            .Where(i => i.QuotationId == quotationId && i.UserId == userId)
            .Select(i => new { i.Id, i.InvoiceNumber })
            .FirstOrDefaultAsync(ct);

        if (existing is not null)
            throw ApiException.Conflict($"Invoice {existing.InvoiceNumber} already exists for this quotation.");

        var currency = await GetCurrencyAsync(userId, ct);
        var invoiceDate = Today;
        var invoiceId = Guid.NewGuid();

        var invoice = new Invoice
        {
            Id = invoiceId,
            UserId = userId,
            QuotationId = quotation.Id,
            CustomerId = quotation.CustomerId,
            InvoiceDate = invoiceDate,
            DueDate = invoiceDate.AddDays(DefaultPaymentTermDays),
            Status = InvoiceStatus.Draft,
            Currency = currency,
            Notes = quotation.Notes,
            Terms = quotation.Terms,
            // Billing snapshot: the customer row may be edited or renamed after this point.
            CustomerName = quotation.Customer!.Name,
            CustomerCompanyName = quotation.Customer.CompanyName,
            CustomerEmail = quotation.Customer.Email,
            CustomerPhone = quotation.Customer.Phone,
            CustomerAddressLine = quotation.Customer.AddressLine,
            CustomerCity = quotation.Customer.City,
            CustomerState = quotation.Customer.State,
            CustomerPostalCode = quotation.Customer.PostalCode,
            CustomerCountry = quotation.Customer.Country
        };

        var order = 0;
        foreach (var line in quotation.Items.OrderBy(i => i.SortOrder))
        {
            invoice.Items.Add(new InvoiceItem
            {
                Id = Guid.NewGuid(),
                InvoiceId = invoiceId,
                SortOrder = order++,
                Name = line.Name,
                Description = line.Description,
                Unit = line.Unit,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                Discount = line.Discount,
                TaxRate = line.TaxRate
            });
        }

        // Recomputed rather than copied across, so the invoice owns its own authoritative figures.
        // The rules match QuotationCalculator, so identical inputs reproduce the quoted totals —
        // which the guard below asserts rather than assumes.
        InvoiceCalculator.ApplyTotals(invoice);

        if (invoice.GrandTotal != quotation.GrandTotal)
            throw new ApiException(System.Net.HttpStatusCode.InternalServerError,
                "The invoice total did not match the accepted quotation. Nothing was created.");

        // Number allocation and the write share one transaction: a failure anywhere leaves the
        // quotation untouched and no half-built invoice behind.
        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);

            var sequence = await NextSequenceAsync(userId, ct);
            invoice.Sequence = sequence;
            invoice.InvoiceNumber = FormatNumber(sequence);

            _db.Invoices.Add(invoice);
            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        });

        return await GetAsync(userId, invoice.Id, ct);
    }

    // ---- reads ---------------------------------------------------------

    public async Task<PagedResult<InvoiceListItemDto>> ListAsync(
        Guid userId, string? search, string? status, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        // Every query starts from the authenticated user's rows.
        var query = _db.Invoices.AsNoTracking().Where(i => i.UserId == userId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(i =>
                i.InvoiceNumber.Contains(term) ||
                i.CustomerName.Contains(term) ||
                i.CustomerCompanyName!.Contains(term) ||
                i.Quotation!.QuotationNumber.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<InvoiceStatus>(status, true, out var parsed))
            query = query.Where(i => i.Status == parsed);

        var total = await query.CountAsync(ct);
        var today = Today;

        var rows = await query
            .OrderByDescending(i => i.Sequence)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(i => new
            {
                i.Id,
                i.InvoiceNumber,
                i.CustomerName,
                i.InvoiceDate,
                i.DueDate,
                i.Status,
                i.GrandTotal,
                i.Currency,
                QuotationNumber = i.Quotation!.QuotationNumber
            })
            .ToListAsync(ct);

        var items = rows.Select(i => new InvoiceListItemDto
        {
            Id = i.Id,
            InvoiceNumber = i.InvoiceNumber,
            CustomerName = i.CustomerName,
            InvoiceDate = i.InvoiceDate,
            DueDate = i.DueDate,
            Status = i.Status.ToString(),
            GrandTotal = i.GrandTotal,
            Currency = i.Currency,
            IsOverdue = i.Status is not (InvoiceStatus.Paid or InvoiceStatus.Cancelled) && i.DueDate < today,
            QuotationNumber = i.QuotationNumber
        }).ToList();

        return new PagedResult<InvoiceListItemDto>(items, page, pageSize, total);
    }

    public async Task<InvoiceDto> GetAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var invoice = await LoadAsync(userId, id, tracking: false, ct);
        var business = await _db.BusinessProfiles.AsNoTracking().FirstOrDefaultAsync(b => b.UserId == userId, ct);
        return Map(invoice, business, Today);
    }

    public async Task<Invoice> GetEntityForPdfAsync(Guid userId, Guid id, CancellationToken ct = default)
        => await LoadAsync(userId, id, tracking: false, ct);

    // ---- writes --------------------------------------------------------

    public async Task<InvoiceDto> UpdateAsync(Guid userId, Guid id, SaveInvoiceRequest request, CancellationToken ct = default)
    {
        // Loaded without items so a rebuilt line set can't collide with tracked originals.
        var invoice = await _db.Invoices.FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId, ct)
                      ?? throw ApiException.NotFound("Invoice");

        if (invoice.IsLocked)
            throw ApiException.Conflict(
                $"{invoice.InvoiceNumber} is {invoice.Status} and can no longer be changed.");

        if (request.DueDate < request.InvoiceDate)
            throw ApiException.BadRequest("Due date must be on or after the invoice date.");

        var wasDraft = invoice.AllowsFinancialEdits;

        invoice.InvoiceDate = request.InvoiceDate;
        invoice.DueDate = request.DueDate;
        invoice.Notes = request.Notes;
        invoice.Terms = request.Terms;
        invoice.Status = ParseStatus(request.Status, invoice.Status);

        if (request.Items is not null)
        {
            // Rewriting the money is a draft-only privilege; an issued invoice keeps the figures
            // the customer received. V2.3 will narrow this further once payments exist.
            if (!wasDraft)
                throw ApiException.Conflict(
                    $"{invoice.InvoiceNumber} has already been issued, so its line items cannot be changed.");

            ValidateItems(request.Items);

            await _db.InvoiceItems.Where(i => i.InvoiceId == id).ExecuteDeleteAsync(ct);

            var order = 0;
            var items = request.Items.Select(item => new InvoiceItem
            {
                Id = Guid.NewGuid(),
                InvoiceId = id,
                SortOrder = order++,
                Name = item.Name.Trim(),
                Description = item.Description,
                Unit = string.IsNullOrWhiteSpace(item.Unit) ? "Service" : item.Unit.Trim(),
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                Discount = item.Discount,
                TaxRate = item.TaxRate
            }).ToList();

            InvoiceCalculator.ApplyTotals(invoice, items);
            _db.InvoiceItems.AddRange(items);
        }

        await _db.SaveChangesAsync(ct);
        return await GetAsync(userId, id, ct);
    }

    public async Task DeleteAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var invoice = await _db.Invoices.FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId, ct)
                      ?? throw ApiException.NotFound("Invoice");

        // A paid or partly paid invoice is an accounting record. Cancel it instead of erasing it.
        if (invoice.Status is InvoiceStatus.Paid or InvoiceStatus.PartiallyPaid)
            throw ApiException.Conflict(
                $"{invoice.InvoiceNumber} is {invoice.Status} and cannot be deleted. Cancel it instead.");

        _db.Invoices.Remove(invoice);
        await _db.SaveChangesAsync(ct);
    }

    // ---- helpers -------------------------------------------------------

    private async Task<Invoice> LoadAsync(Guid userId, Guid id, bool tracking, CancellationToken ct)
    {
        var query = _db.Invoices
            .Include(i => i.Quotation)
            .Include(i => i.Items.OrderBy(x => x.SortOrder))
            .Where(i => i.Id == id && i.UserId == userId);

        if (!tracking) query = query.AsNoTracking();

        // An invoice belonging to another user is reported as 404, not 403, so ids can't be probed.
        return await query.FirstOrDefaultAsync(ct) ?? throw ApiException.NotFound("Invoice");
    }

    private static void ValidateItems(List<SaveInvoiceItemRequest> items)
    {
        if (items.Count == 0)
            throw ApiException.BadRequest("An invoice needs at least one item.");

        foreach (var item in items)
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

    private static InvoiceStatus ParseStatus(string? status, InvoiceStatus fallback)
    {
        if (string.IsNullOrWhiteSpace(status)) return fallback;
        if (!Enum.TryParse<InvoiceStatus>(status, true, out var parsed))
            throw ApiException.BadRequest("Unknown invoice status.");
        return parsed;
    }

    /// <summary>Invoice numbers are sequential per user, so two businesses both start at INV-000001.</summary>
    private async Task<int> NextSequenceAsync(Guid userId, CancellationToken ct)
    {
        var last = await _db.Invoices.AsNoTracking()
            .Where(i => i.UserId == userId)
            .OrderByDescending(i => i.Sequence)
            .Select(i => (int?)i.Sequence)
            .FirstOrDefaultAsync(ct);
        return (last ?? 0) + 1;
    }

    public static string FormatNumber(int sequence) => $"INV-{sequence:D6}";

    private async Task<string> GetCurrencyAsync(Guid userId, CancellationToken ct) =>
        await _db.BusinessProfiles.AsNoTracking()
            .Where(b => b.UserId == userId)
            .Select(b => b.Currency)
            .FirstOrDefaultAsync(ct) ?? "INR";

    public static InvoiceDto Map(Invoice invoice, BusinessProfile? business, DateOnly today) => new()
    {
        Id = invoice.Id,
        InvoiceNumber = invoice.InvoiceNumber,
        QuotationId = invoice.QuotationId,
        QuotationNumber = invoice.Quotation?.QuotationNumber ?? string.Empty,
        Customer = new InvoiceCustomerDto
        {
            Name = invoice.CustomerName,
            CompanyName = invoice.CustomerCompanyName,
            Email = invoice.CustomerEmail,
            Phone = invoice.CustomerPhone,
            AddressLine = invoice.CustomerAddressLine,
            City = invoice.CustomerCity,
            State = invoice.CustomerState,
            PostalCode = invoice.CustomerPostalCode,
            Country = invoice.CustomerCountry
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
        InvoiceDate = invoice.InvoiceDate,
        DueDate = invoice.DueDate,
        Status = invoice.Status.ToString(),
        IsOverdue = invoice.IsOverdue(today),
        CanEdit = !invoice.IsLocked,
        CanEditItems = invoice.AllowsFinancialEdits,
        CanDelete = invoice.Status is not (InvoiceStatus.Paid or InvoiceStatus.PartiallyPaid),
        Notes = invoice.Notes,
        Terms = invoice.Terms,
        Subtotal = invoice.Subtotal,
        DiscountTotal = invoice.DiscountTotal,
        TaxTotal = invoice.TaxTotal,
        GrandTotal = invoice.GrandTotal,
        Currency = invoice.Currency,
        Items = invoice.Items.OrderBy(i => i.SortOrder).Select(i => new InvoiceItemDto
        {
            Id = i.Id,
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
        CreatedAt = invoice.CreatedAt,
        UpdatedAt = invoice.UpdatedAt
    };
}
