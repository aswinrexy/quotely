using Microsoft.EntityFrameworkCore;
using Quotely.Api.Data;
using Quotely.Api.DTOs;
using Quotely.Api.Middleware;
using Quotely.Api.Models;

namespace Quotely.Api.Services;

public interface ITradeInvoiceService
{
    Task<PagedResult<TradeInvoiceListItemDto>> ListAsync(
        Guid userId, string? search, string? tradeType, string? documentType, string? status,
        Guid? customerId, int page, int pageSize, CancellationToken ct = default);

    Task<TradeInvoiceDto> GetAsync(Guid userId, Guid id, CancellationToken ct = default);
    Task<TradeInvoiceDto> CreateAsync(Guid userId, SaveTradeInvoiceRequest request, CancellationToken ct = default);
    Task<TradeInvoiceDto> UpdateAsync(Guid userId, Guid id, SaveTradeInvoiceRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid userId, Guid id, CancellationToken ct = default);
    Task<Invoice> GetEntityForPdfAsync(Guid userId, Guid id, CancellationToken ct = default);
}

/// <summary>
/// Import/export invoices.
///
/// The document is an ordinary <see cref="Invoice"/> with <see cref="InvoiceType.ImportExport"/>
/// and a <see cref="TradeInvoiceDetails"/> row beside it. That is not a detail — it is the reason
/// payments, the public link, sharing, the receivables report, tenant isolation and the invoice
/// number sequence all keep working here without a line of new code. Nothing in this file
/// re-implements any of them.
///
/// What IS here is the part a domestic invoice has no concept of: the parties, the shipment, the
/// regulatory identifiers, and the goods lines that carry weights and HS codes.
/// </summary>
public class TradeInvoiceService : ITradeInvoiceService
{
    private const int DefaultPaymentTermDays = 15;

    private readonly AppDbContext _db;
    private readonly ITradeProfileService _profiles;

    public TradeInvoiceService(AppDbContext db, ITradeProfileService profiles)
    {
        _db = db;
        _profiles = profiles;
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    // ---- reading --------------------------------------------------------

    public async Task<PagedResult<TradeInvoiceListItemDto>> ListAsync(
        Guid userId, string? search, string? tradeType, string? documentType, string? status,
        Guid? customerId, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        // Type is part of the WHERE, not a post-filter: a business with ten thousand domestic
        // invoices and forty trade documents must not pay for the ten thousand.
        var query = _db.Invoices.AsNoTracking()
            .Where(i => i.UserId == userId && i.Type == InvoiceType.ImportExport)
            .Include(i => i.TradeDetails)
            .AsQueryable();

        if (customerId is { } cid) query = query.Where(i => i.CustomerId == cid);

        if (!string.IsNullOrWhiteSpace(tradeType))
            query = query.Where(i => i.TradeDetails!.TradeType == ParseTradeType(tradeType));

        if (!string.IsNullOrWhiteSpace(documentType))
            query = query.Where(i => i.TradeDetails!.DocumentType == ParseDocumentType(documentType));

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(i => i.Status == ParseStatus(status, InvoiceStatus.Draft));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(i =>
                EF.Functions.Like(i.InvoiceNumber, $"%{term}%")
                || EF.Functions.Like(i.TradeDetails!.DocumentNumber ?? string.Empty, $"%{term}%")
                || EF.Functions.Like(i.TradeDetails!.ConsigneeName, $"%{term}%")
                || EF.Functions.Like(i.TradeDetails!.FinalDestination ?? string.Empty, $"%{term}%"));
        }

        var total = await query.CountAsync(ct);

        var rows = await query
            .OrderByDescending(i => i.InvoiceDate).ThenByDescending(i => i.Sequence)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct);

        var items = rows.Select(i => new TradeInvoiceListItemDto
        {
            Id = i.Id,
            InvoiceNumber = i.InvoiceNumber,
            // The exporter's own series when they keep one, otherwise ours. The column always has
            // something in it, which matters more here than showing an empty cell honestly.
            DocumentNumber = string.IsNullOrWhiteSpace(i.TradeDetails?.DocumentNumber)
                ? i.InvoiceNumber
                : i.TradeDetails!.DocumentNumber!,
            TradeType = (i.TradeDetails?.TradeType ?? Models.TradeType.Export).ToString(),
            DocumentType = (i.TradeDetails?.DocumentType ?? TradeDocumentType.ProformaInvoice).ToString(),
            ConsigneeName = i.TradeDetails?.ConsigneeName ?? i.CustomerName,
            FinalDestination = i.TradeDetails?.FinalDestination,
            InvoiceDate = i.InvoiceDate,
            Status = i.Status.ToString(),
            Currency = i.Currency,
            GrandTotal = i.GrandTotal,
            TotalPackages = i.TradeDetails?.TotalPackages ?? 0m,
            TotalNetWeight = i.TradeDetails?.TotalNetWeight ?? 0m,
            IsPayable = i.TradeDetails?.DefaultPayable ?? true,
        }).ToList();

        return new PagedResult<TradeInvoiceListItemDto>(items, page, pageSize, total);
    }

    public async Task<TradeInvoiceDto> GetAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var invoice = await LoadAsync(userId, id, tracking: false, ct);
        var business = await _db.BusinessProfiles.AsNoTracking()
            .FirstOrDefaultAsync(b => b.UserId == userId, ct);
        return Map(invoice, business);
    }

    public async Task<Invoice> GetEntityForPdfAsync(Guid userId, Guid id, CancellationToken ct = default)
        => await LoadAsync(userId, id, tracking: false, ct);

    // ---- writing --------------------------------------------------------

    public async Task<TradeInvoiceDto> CreateAsync(
        Guid userId, SaveTradeInvoiceRequest request, CancellationToken ct = default)
    {
        Validate(request);

        // Ownership first, and a 404 rather than a 403 for someone else's customer — the same
        // rule the rest of Quotely follows, so ids cannot be probed from this endpoint either.
        var customer = await _db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.CustomerId && c.UserId == userId, ct)
            ?? throw ApiException.NotFound("Customer");

        var defaults = await _profiles.GetDefaultsAsync(userId, ct);

        var invoiceId = Guid.NewGuid();
        var invoice = new Invoice
        {
            Id = invoiceId,
            UserId = userId,
            Type = InvoiceType.ImportExport,
            QuotationId = null,
            CustomerId = customer.Id,
            InvoiceDate = request.InvoiceDate,
            DueDate = request.DueDate ?? request.InvoiceDate.AddDays(DefaultPaymentTermDays),
            Status = InvoiceStatus.Draft,
            Currency = NormaliseCurrency(request.Currency) ?? defaults.Currency,
            Notes = Trim(request.Notes),
        };

        if (invoice.DueDate < invoice.InvoiceDate)
            throw ApiException.BadRequest("Due date must be on or after the invoice date.");

        ApplyCustomerSnapshot(invoice, customer);

        var details = new TradeInvoiceDetails { Id = Guid.NewGuid(), InvoiceId = invoiceId };
        ApplyDetails(details, request, customer, defaults);
        invoice.TradeDetails = details;

        foreach (var item in BuildItems(invoiceId, request)) invoice.Items.Add(item);
        InvoiceCalculator.ApplyTotals(invoice);
        ApplyGoodsTotals(details, invoice.Items);

        await InvoiceNumbering.NumberAndInsertAsync(_db, userId, invoice, ct);

        return await GetAsync(userId, invoice.Id, ct);
    }

    public async Task<TradeInvoiceDto> UpdateAsync(
        Guid userId, Guid id, SaveTradeInvoiceRequest request, CancellationToken ct = default)
    {
        Validate(request);

        // Loaded WITHOUT its lines. ExecuteDelete below removes them straight from the database,
        // and any line still sitting in the change tracker would then be saved against a row that
        // no longer exists — an optimistic-concurrency failure on an edit that was perfectly valid.
        var invoice = await LoadForWriteAsync(userId, id, ct);

        if (invoice.IsLocked)
            throw ApiException.BadRequest("A paid or cancelled invoice can no longer be edited.");

        var customer = await _db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.CustomerId && c.UserId == userId, ct)
            ?? throw ApiException.NotFound("Customer");

        var defaults = await _profiles.GetDefaultsAsync(userId, ct);

        invoice.InvoiceDate = request.InvoiceDate;
        invoice.DueDate = request.DueDate ?? invoice.DueDate;
        if (invoice.DueDate < invoice.InvoiceDate)
            throw ApiException.BadRequest("Due date must be on or after the invoice date.");

        invoice.Status = ParseStatus(request.Status, invoice.Status);
        invoice.Notes = Trim(request.Notes);
        invoice.CustomerId = customer.Id;
        ApplyCustomerSnapshot(invoice, customer);

        var currency = NormaliseCurrency(request.Currency);
        if (currency is not null) invoice.Currency = currency;

        var details = invoice.TradeDetails
                      ?? throw ApiException.BadRequest("This invoice has no import/export detail.");
        ApplyDetails(details, request, customer, defaults);
        details.UpdatedAt = DateTime.UtcNow;

        // Financial edits follow the ordinary invoice rule: lines may only be rewritten while the
        // document is still a draft. Once it has gone out, the figures the consignee received —
        // and that a customs broker may already be holding — are history.
        if (invoice.AllowsFinancialEdits)
        {
            // The trade detail rows cascade with their lines at the database level.
            await _db.InvoiceItems.Where(i => i.InvoiceId == invoice.Id).ExecuteDeleteAsync(ct);

            var items = BuildItems(invoice.Id, request);
            InvoiceCalculator.ApplyTotals(invoice, items);
            ApplyGoodsTotals(details, items);
            _db.InvoiceItems.AddRange(items);
        }

        invoice.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return await GetAsync(userId, id, ct);
    }

    public async Task DeleteAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var invoice = await LoadAsync(userId, id, tracking: true, ct);

        if (invoice.Status != InvoiceStatus.Draft)
            throw ApiException.BadRequest("Only a draft document can be deleted.");

        var hasPayments = await _db.Payments.AnyAsync(p => p.InvoiceId == invoice.Id, ct);
        if (hasPayments)
            throw ApiException.BadRequest("This document has payments recorded against it.");

        // The trade detail and the lines cascade; nothing to delete by hand.
        _db.Invoices.Remove(invoice);
        await _db.SaveChangesAsync(ct);
    }

    // ---- building -------------------------------------------------------

    private static void ApplyDetails(
        TradeInvoiceDetails details,
        SaveTradeInvoiceRequest request,
        Customer customer,
        TradeInvoiceDefaultsDto defaults)
    {
        details.TradeType = ParseTradeType(request.TradeType);
        details.DocumentType = ParseDocumentType(request.DocumentType);

        details.DocumentNumber = Trim(request.DocumentNumber);
        details.BuyerOrderNumber = Trim(request.BuyerOrderNumber);
        details.BuyerOrderDate = request.BuyerOrderDate;
        details.OtherReferences = Trim(request.OtherReferences);

        // Precedence, stated once and applied everywhere: what the document says, then what the
        // trade profile says, then nothing. An explicit value on the document always wins, because
        // the person filling it in knows something about this shipment that the settings do not.
        details.PartyName = Trim(request.PartyName) ?? defaults.PartyName ?? string.Empty;
        details.PartyAddress = Trim(request.PartyAddress) ?? defaults.PartyAddress;

        // The consignee defaults to the selected customer, which is the whole point of picking one.
        details.ConsigneeName = Trim(request.ConsigneeName) ?? customer.Name;
        details.ConsigneeAddress = Trim(request.ConsigneeAddress) ?? ComposeCustomerAddress(customer);

        details.BuyerSameAsConsignee = request.BuyerSameAsConsignee;
        // Cleared rather than kept when the buyer IS the consignee: leaving a stale buyer address
        // in the row means the next edit that unticks the box silently resurrects it.
        details.BuyerName = request.BuyerSameAsConsignee ? null : Trim(request.BuyerName);
        details.BuyerAddress = request.BuyerSameAsConsignee ? null : Trim(request.BuyerAddress);

        details.NotifyPartyName = Trim(request.NotifyPartyName);
        details.NotifyPartyAddress = Trim(request.NotifyPartyAddress);

        details.PreCarriageBy = Trim(request.PreCarriageBy) ?? defaults.PreCarriageBy;
        details.PlaceOfReceipt = Trim(request.PlaceOfReceipt);
        details.VesselOrFlightNumber = Trim(request.VesselOrFlightNumber);
        details.PortOfLoading = Trim(request.PortOfLoading) ?? defaults.PortOfLoading;
        details.PortOfDischarge = Trim(request.PortOfDischarge);
        details.FinalDestination = Trim(request.FinalDestination);
        details.CountryOfOrigin = Trim(request.CountryOfOrigin) ?? defaults.CountryOfOrigin;
        details.CountryOfFinalDestination = Trim(request.CountryOfFinalDestination);

        details.TermsOfDelivery = Trim(request.TermsOfDelivery) ?? defaults.TermsOfDelivery;
        details.TermsOfPayment = Trim(request.TermsOfPayment) ?? defaults.TermsOfPayment;
        details.PricingTerm = Trim(request.PricingTerm) ?? defaults.PricingTerm;

        details.IecNumber = Trim(request.IecNumber) ?? defaults.IecNumber;
        details.GstNumber = Trim(request.GstNumber) ?? defaults.GstNumber;
        details.PanNumber = Trim(request.PanNumber) ?? defaults.PanNumber;
        details.ApedaRegistrationNumber =
            Trim(request.ApedaRegistrationNumber) ?? defaults.ApedaRegistrationNumber;
        details.ApedaValidUntil = request.ApedaValidUntil ?? defaults.ApedaValidUntil;

        details.HeaderDeclarations = Trim(request.HeaderDeclarations) ?? defaults.HeaderDeclarations;
        details.FooterDeclaration = Trim(request.FooterDeclaration) ?? defaults.FooterDeclaration;
        details.AuthorisedSignatory = Trim(request.AuthorisedSignatory) ?? defaults.AuthorisedSignatory;

        details.WeightUnit = Trim(request.WeightUnit) ?? "KGS";
    }

    private static List<InvoiceItem> BuildItems(Guid invoiceId, SaveTradeInvoiceRequest request)
    {
        var items = new List<InvoiceItem>();
        var order = 0;

        foreach (var line in request.Items)
        {
            var itemId = Guid.NewGuid();
            var item = new InvoiceItem
            {
                Id = itemId,
                InvoiceId = invoiceId,
                SortOrder = order++,
                Name = line.Description.Trim(),
                Description = Trim(line.Detail),
                // The packaging unit doubles as the invoice line's Unit, so a trade line still
                // reads sensibly anywhere the ordinary invoice machinery displays it.
                Unit = Trim(line.QuantityUnit) ?? "NOS",
                Quantity = line.Quantity,
                UnitPrice = line.Rate,
                Discount = line.Discount,
                TaxRate = line.TaxRate,
                TradeDetails = new TradeLineDetails
                {
                    Id = Guid.NewGuid(),
                    InvoiceItemId = itemId,
                    MarksAndNumbers = Trim(line.MarksAndNumbers),
                    Dimension = Trim(line.Dimension),
                    HsCode = Trim(line.HsCode),
                    NetWeight = line.NetWeight,
                    GrossWeight = line.GrossWeight,
                    QuantityUnit = Trim(line.QuantityUnit),
                    RateBasis = ParseRateBasis(line.RateBasis),
                    RateLabel = Trim(line.RateLabel),
                },
            };

            items.Add(item);
        }

        return items;
    }

    /// <summary>
    /// The invoice and its trade detail, tracked, with the lines deliberately left out. See the
    /// note at the call site — the lines are replaced wholesale, not diffed.
    /// </summary>
    private async Task<Invoice> LoadForWriteAsync(Guid userId, Guid id, CancellationToken ct) =>
        await _db.Invoices
            .Include(i => i.TradeDetails)
            .FirstOrDefaultAsync(
                i => i.Id == id && i.UserId == userId && i.Type == InvoiceType.ImportExport, ct)
        ?? throw ApiException.NotFound("Import/export invoice");

    /// <summary>
    /// The shipment totals, computed on the server for the same reason the money is: the browser
    /// shows a preview, the document states a fact. A customs declaration that disagrees with the
    /// sum of its own lines is worse than one that is merely wrong.
    /// </summary>
    private static void ApplyGoodsTotals(TradeInvoiceDetails details, IEnumerable<InvoiceItem> items)
    {
        decimal net = 0, gross = 0, packages = 0;

        foreach (var item in items)
        {
            net += item.TradeDetails?.NetWeight ?? 0m;
            gross += item.TradeDetails?.GrossWeight ?? 0m;
            packages += item.Quantity;
        }

        details.TotalNetWeight = decimal.Round(net, 3, MidpointRounding.AwayFromZero);
        details.TotalGrossWeight = decimal.Round(gross, 3, MidpointRounding.AwayFromZero);
        details.TotalPackages = decimal.Round(packages, 3, MidpointRounding.AwayFromZero);
    }

    // ---- validation -----------------------------------------------------

    /// <summary>
    /// What a document genuinely needs to exist, and nothing more.
    ///
    /// Deliberately NOT a customs compliance check. Quotely does not know which fields a given
    /// consignment, country or commodity requires, and refusing to save a document because a
    /// country of origin is blank would be asserting an authority it does not have. Fields that
    /// customs generally expects are surfaced by the form, not enforced by the server.
    /// </summary>
    private static void Validate(SaveTradeInvoiceRequest request)
    {
        if (request.Items is null || request.Items.Count == 0)
            throw ApiException.BadRequest("A trade document needs at least one line of goods.");

        foreach (var line in request.Items)
        {
            var label = string.IsNullOrWhiteSpace(line.Description) ? "A line" : $"\"{line.Description.Trim()}\"";

            if (string.IsNullOrWhiteSpace(line.Description))
                throw ApiException.BadRequest("Every line needs a description of goods.");
            if (line.Quantity <= 0)
                throw ApiException.BadRequest($"Quantity for {label} must be greater than zero.");
            if (line.Rate < 0)
                throw ApiException.BadRequest($"Rate for {label} cannot be negative.");
            if (line.NetWeight is < 0)
                throw ApiException.BadRequest($"Net weight for {label} cannot be negative.");
            if (line.GrossWeight is < 0)
                throw ApiException.BadRequest($"Gross weight for {label} cannot be negative.");
            if (line.Discount < 0)
                throw ApiException.BadRequest($"Discount for {label} cannot be negative.");
            if (line.TaxRate is < 0 or > 100)
                throw ApiException.BadRequest($"Tax rate for {label} must be between 0 and 100.");

            // Gross is the packed weight — goods plus packaging — so it cannot be the lighter of
            // the two. This one IS worth refusing: it is arithmetic, not regulation, and a
            // consignment whose gross is under its net will be queried by whoever receives it.
            if (line.NetWeight is { } n && line.GrossWeight is { } g && g < n)
                throw ApiException.BadRequest(
                    $"Gross weight for {label} cannot be less than its net weight.");

            // Pricing by weight with no weight would silently total zero.
            if (ParseRateBasis(line.RateBasis) == TradeRateBasis.PerNetWeight
                && (line.NetWeight is null || line.NetWeight <= 0))
                throw ApiException.BadRequest(
                    $"{label} is priced per unit of weight, so it needs a net weight.");
        }
    }

    // ---- plumbing -------------------------------------------------------

    private async Task<Invoice> LoadAsync(Guid userId, Guid id, bool tracking, CancellationToken ct)
    {
        var query = _db.Invoices
            .Include(i => i.TradeDetails)
            .Include(i => i.Items.OrderBy(x => x.SortOrder))
                .ThenInclude(item => item.TradeDetails)
            .Where(i => i.Id == id && i.UserId == userId && i.Type == InvoiceType.ImportExport);

        if (!tracking) query = query.AsNoTracking();

        // Another tenant's document — and a STANDARD invoice reached through this endpoint — are
        // both 404. The second matters: these routes must not become a side door for editing a
        // domestic invoice with a form that has no idea about its tax lines.
        return await query.FirstOrDefaultAsync(ct)
               ?? throw ApiException.NotFound("Import/export invoice");
    }

    private static void ApplyCustomerSnapshot(Invoice invoice, Customer customer)
    {
        invoice.CustomerName = customer.Name;
        invoice.CustomerCompanyName = customer.CompanyName;
        invoice.CustomerEmail = customer.Email;
        invoice.CustomerPhone = customer.Phone;
        invoice.CustomerAddressLine = customer.AddressLine;
        invoice.CustomerCity = customer.City;
        invoice.CustomerState = customer.State;
        invoice.CustomerPostalCode = customer.PostalCode;
        invoice.CustomerCountry = customer.Country;
    }

    public static string? ComposeCustomerAddress(Customer customer)
    {
        var parts = new[]
        {
            customer.AddressLine, customer.City, customer.State, customer.PostalCode, customer.Country,
        }.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim());

        var joined = string.Join(", ", parts);
        return string.IsNullOrWhiteSpace(joined) ? null : joined;
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormaliseCurrency(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    public static TradeType ParseTradeType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Models.TradeType.Export;
        if (!Enum.TryParse<TradeType>(value, true, out var parsed))
            throw ApiException.BadRequest("Trade type must be Export or Import.");
        return parsed;
    }

    public static TradeDocumentType ParseDocumentType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return TradeDocumentType.ProformaInvoice;
        if (!Enum.TryParse<TradeDocumentType>(value, true, out var parsed))
            throw ApiException.BadRequest("Document type must be ProformaInvoice or CommercialInvoice.");
        return parsed;
    }

    public static TradeRateBasis ParseRateBasis(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return TradeRateBasis.PerQuantityUnit;
        if (!Enum.TryParse<TradeRateBasis>(value, true, out var parsed))
            throw ApiException.BadRequest("Rate basis must be PerQuantityUnit or PerNetWeight.");
        return parsed;
    }

    private static InvoiceStatus ParseStatus(string? status, InvoiceStatus fallback)
    {
        if (string.IsNullOrWhiteSpace(status)) return fallback;
        if (!Enum.TryParse<InvoiceStatus>(status, true, out var parsed))
            throw ApiException.BadRequest("Unknown invoice status.");
        return parsed;
    }

    // ---- mapping --------------------------------------------------------

    public static TradeInvoiceDto Map(Invoice invoice, BusinessProfile? business)
    {
        var d = invoice.TradeDetails;

        return new TradeInvoiceDto
        {
            Id = invoice.Id,
            InvoiceNumber = invoice.InvoiceNumber,
            DocumentNumber = d?.DocumentNumber,
            TradeType = (d?.TradeType ?? Models.TradeType.Export).ToString(),
            DocumentType = (d?.DocumentType ?? TradeDocumentType.ProformaInvoice).ToString(),

            InvoiceDate = invoice.InvoiceDate,
            DueDate = invoice.DueDate,
            Status = invoice.Status.ToString(),
            CanEdit = !invoice.IsLocked,
            CanEditItems = invoice.AllowsFinancialEdits,
            CanDelete = invoice.Status == InvoiceStatus.Draft,
            IsPayable = d?.DefaultPayable ?? true,

            CustomerId = invoice.CustomerId,

            BuyerOrderNumber = d?.BuyerOrderNumber,
            BuyerOrderDate = d?.BuyerOrderDate,
            OtherReferences = d?.OtherReferences,

            PartyName = d?.PartyName ?? string.Empty,
            PartyAddress = d?.PartyAddress,
            ConsigneeName = d?.ConsigneeName ?? invoice.CustomerName,
            ConsigneeAddress = d?.ConsigneeAddress,
            BuyerSameAsConsignee = d?.BuyerSameAsConsignee ?? true,
            BuyerName = d?.BuyerName,
            BuyerAddress = d?.BuyerAddress,
            NotifyPartyName = d?.NotifyPartyName,
            NotifyPartyAddress = d?.NotifyPartyAddress,

            PreCarriageBy = d?.PreCarriageBy,
            PlaceOfReceipt = d?.PlaceOfReceipt,
            VesselOrFlightNumber = d?.VesselOrFlightNumber,
            PortOfLoading = d?.PortOfLoading,
            PortOfDischarge = d?.PortOfDischarge,
            FinalDestination = d?.FinalDestination,
            CountryOfOrigin = d?.CountryOfOrigin,
            CountryOfFinalDestination = d?.CountryOfFinalDestination,

            TermsOfDelivery = d?.TermsOfDelivery,
            TermsOfPayment = d?.TermsOfPayment,
            PricingTerm = d?.PricingTerm,

            IecNumber = d?.IecNumber,
            GstNumber = d?.GstNumber,
            PanNumber = d?.PanNumber,
            ApedaRegistrationNumber = d?.ApedaRegistrationNumber,
            ApedaValidUntil = d?.ApedaValidUntil,

            HeaderDeclarations = d?.HeaderDeclarations,
            FooterDeclaration = d?.FooterDeclaration,
            AuthorisedSignatory = d?.AuthorisedSignatory,

            Currency = invoice.Currency,
            Subtotal = invoice.Subtotal,
            DiscountTotal = invoice.DiscountTotal,
            TaxTotal = invoice.TaxTotal,
            GrandTotal = invoice.GrandTotal,
            TotalNetWeight = d?.TotalNetWeight ?? 0m,
            TotalGrossWeight = d?.TotalGrossWeight ?? 0m,
            TotalPackages = d?.TotalPackages ?? 0m,
            WeightUnit = d?.WeightUnit ?? "KGS",
            AmountInWords = AmountInWords.Format(invoice.GrandTotal, invoice.Currency),

            HasPublicLink = invoice.PublicTokenHash is not null,
            Notes = invoice.Notes,

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
                Currency = business.Currency,
            },

            Items = invoice.Items.OrderBy(i => i.SortOrder).Select(i => new TradeLineDto
            {
                Id = i.Id,
                MarksAndNumbers = i.TradeDetails?.MarksAndNumbers,
                Description = i.Name,
                Detail = i.Description,
                Dimension = i.TradeDetails?.Dimension,
                HsCode = i.TradeDetails?.HsCode,
                NetWeight = i.TradeDetails?.NetWeight,
                GrossWeight = i.TradeDetails?.GrossWeight,
                Quantity = i.Quantity,
                QuantityUnit = i.TradeDetails?.QuantityUnit ?? i.Unit,
                Rate = i.UnitPrice,
                RateBasis = (i.TradeDetails?.RateBasis ?? TradeRateBasis.PerQuantityUnit).ToString(),
                RateLabel = i.TradeDetails?.RateLabel,
                Discount = i.Discount,
                TaxRate = i.TaxRate,
                LineTotal = i.LineTotal,
            }).ToList(),
        };
    }
}
