using Microsoft.EntityFrameworkCore;
using Quotely.Api.Data;
using Quotely.Api.DTOs;
using Quotely.Api.Models;

namespace Quotely.Api.Services;

public interface ITradeProfileService
{
    Task<TradeProfile> GetOrCreateAsync(Guid userId, CancellationToken ct = default);
    Task<TradeProfileDto> GetAsync(Guid userId, CancellationToken ct = default);
    Task<TradeProfileDto> SaveAsync(Guid userId, SaveTradeProfileRequest request, CancellationToken ct = default);
    Task<TradeInvoiceDefaultsDto> GetDefaultsAsync(Guid userId, CancellationToken ct = default);
}

/// <summary>
/// The business's import/export settings — entered once, copied onto every document.
///
/// Created lazily on first read, the same way the subscription is: a business that never exports
/// anything should not carry a row of empty customs fields, and asking them to "set up" a profile
/// before they can see the form is a step that teaches nothing.
/// </summary>
public class TradeProfileService : ITradeProfileService
{
    private readonly AppDbContext _db;

    public TradeProfileService(AppDbContext db) => _db = db;

    public async Task<TradeProfile> GetOrCreateAsync(Guid userId, CancellationToken ct = default)
    {
        var profile = await _db.TradeProfiles.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (profile is not null) return profile;

        profile = new TradeProfile { Id = Guid.NewGuid(), UserId = userId };
        _db.TradeProfiles.Add(profile);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two requests raced on first load. The unique index on UserId settled it; read back
            // whichever one won rather than failing a page load over it.
            _db.ChangeTracker.Clear();
            profile = await _db.TradeProfiles.FirstAsync(p => p.UserId == userId, ct);
        }

        return profile;
    }

    public async Task<TradeProfileDto> GetAsync(Guid userId, CancellationToken ct = default) =>
        Map(await GetOrCreateAsync(userId, ct));

    public async Task<TradeProfileDto> SaveAsync(
        Guid userId, SaveTradeProfileRequest request, CancellationToken ct = default)
    {
        var profile = await GetOrCreateAsync(userId, ct);

        profile.IecNumber = Trim(request.IecNumber);
        profile.GstNumber = Trim(request.GstNumber);
        profile.PanNumber = Trim(request.PanNumber);
        profile.ApedaRegistrationNumber = Trim(request.ApedaRegistrationNumber);
        profile.ApedaValidUntil = request.ApedaValidUntil;

        profile.PartyNameOverride = Trim(request.PartyNameOverride);
        profile.PartyAddressOverride = Trim(request.PartyAddressOverride);

        profile.DefaultCountryOfOrigin = Trim(request.DefaultCountryOfOrigin);
        profile.DefaultTermsOfDelivery = Trim(request.DefaultTermsOfDelivery);
        profile.DefaultTermsOfPayment = Trim(request.DefaultTermsOfPayment);
        profile.DefaultPricingTerm = Trim(request.DefaultPricingTerm);
        profile.DefaultPortOfLoading = Trim(request.DefaultPortOfLoading);
        profile.DefaultPreCarriageBy = Trim(request.DefaultPreCarriageBy);

        profile.DefaultHeaderDeclarations = Trim(request.DefaultHeaderDeclarations);
        profile.DefaultFooterDeclaration = Trim(request.DefaultFooterDeclaration);
        profile.DefaultAuthorisedSignatory = Trim(request.DefaultAuthorisedSignatory);
        profile.DefaultCurrency = Trim(request.DefaultCurrency)?.ToUpperInvariant();

        profile.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Map(profile);
    }

    /// <summary>
    /// What a blank trade document should already contain.
    ///
    /// The exporter block falls back through trade profile, then business profile, then empty —
    /// so a business that has filled in neither still gets a usable form, and one that has filled
    /// in the ordinary profile does not have to repeat their own address to export.
    /// </summary>
    public async Task<TradeInvoiceDefaultsDto> GetDefaultsAsync(Guid userId, CancellationToken ct = default)
    {
        var profile = await GetOrCreateAsync(userId, ct);
        var business = await _db.BusinessProfiles.AsNoTracking()
            .FirstOrDefaultAsync(b => b.UserId == userId, ct);

        var next = await InvoiceNumbering.PeekNextSequenceAsync(_db, userId, ct);

        return new TradeInvoiceDefaultsDto
        {
            PartyName = profile.PartyNameOverride ?? business?.BusinessName,
            PartyAddress = profile.PartyAddressOverride ?? ComposeAddress(business),
            CountryOfOrigin = profile.DefaultCountryOfOrigin ?? business?.Country,
            TermsOfDelivery = profile.DefaultTermsOfDelivery,
            TermsOfPayment = profile.DefaultTermsOfPayment,
            PricingTerm = profile.DefaultPricingTerm,
            PortOfLoading = profile.DefaultPortOfLoading,
            PreCarriageBy = profile.DefaultPreCarriageBy,
            IecNumber = profile.IecNumber,
            // The ordinary profile's TaxNumber is where an Indian business already keeps its GST,
            // so fall back to it rather than making them type the same number in two places.
            GstNumber = profile.GstNumber ?? business?.TaxNumber,
            PanNumber = profile.PanNumber,
            ApedaRegistrationNumber = profile.ApedaRegistrationNumber,
            ApedaValidUntil = profile.ApedaValidUntil,
            HeaderDeclarations = profile.DefaultHeaderDeclarations,
            FooterDeclaration = profile.DefaultFooterDeclaration,
            AuthorisedSignatory = profile.DefaultAuthorisedSignatory ?? business?.BusinessName,
            Currency = profile.DefaultCurrency ?? business?.Currency ?? "INR",
            SuggestedDocumentNumber = InvoiceNumbering.Format(next),
        };
    }

    /// <summary>The business address as one printable block, matching how the PDF renders it.</summary>
    public static string? ComposeAddress(BusinessProfile? business)
    {
        if (business is null) return null;

        var parts = new[]
        {
            business.AddressLine,
            business.City,
            business.State,
            business.PostalCode,
            business.Country,
        }.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim());

        var joined = string.Join(", ", parts);
        return string.IsNullOrWhiteSpace(joined) ? null : joined;
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static TradeProfileDto Map(TradeProfile p) => new()
    {
        IecNumber = p.IecNumber,
        GstNumber = p.GstNumber,
        PanNumber = p.PanNumber,
        ApedaRegistrationNumber = p.ApedaRegistrationNumber,
        ApedaValidUntil = p.ApedaValidUntil,
        PartyNameOverride = p.PartyNameOverride,
        PartyAddressOverride = p.PartyAddressOverride,
        DefaultCountryOfOrigin = p.DefaultCountryOfOrigin,
        DefaultTermsOfDelivery = p.DefaultTermsOfDelivery,
        DefaultTermsOfPayment = p.DefaultTermsOfPayment,
        DefaultPricingTerm = p.DefaultPricingTerm,
        DefaultPortOfLoading = p.DefaultPortOfLoading,
        DefaultPreCarriageBy = p.DefaultPreCarriageBy,
        DefaultHeaderDeclarations = p.DefaultHeaderDeclarations,
        DefaultFooterDeclaration = p.DefaultFooterDeclaration,
        DefaultAuthorisedSignatory = p.DefaultAuthorisedSignatory,
        DefaultCurrency = p.DefaultCurrency,
        SuggestedHeaderDeclarations = TradeProfile.SuggestedHeaderDeclarations,
        SuggestedFooterDeclaration = TradeProfile.SuggestedFooterDeclaration,
    };
}
