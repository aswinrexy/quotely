using System.ComponentModel.DataAnnotations;

namespace Quotely.Api.DTOs;

public record TradeProfileDto
{
    public string? IecNumber { get; init; }
    public string? GstNumber { get; init; }
    public string? PanNumber { get; init; }
    public string? ApedaRegistrationNumber { get; init; }
    public DateOnly? ApedaValidUntil { get; init; }

    public string? PartyNameOverride { get; init; }
    public string? PartyAddressOverride { get; init; }

    public string? DefaultCountryOfOrigin { get; init; }
    public string? DefaultTermsOfDelivery { get; init; }
    public string? DefaultTermsOfPayment { get; init; }
    public string? DefaultPricingTerm { get; init; }
    public string? DefaultPortOfLoading { get; init; }
    public string? DefaultPreCarriageBy { get; init; }

    public string? DefaultHeaderDeclarations { get; init; }
    public string? DefaultFooterDeclaration { get; init; }
    public string? DefaultAuthorisedSignatory { get; init; }
    public string? DefaultCurrency { get; init; }

    /// <summary>
    /// Wording Quotely offers as a starting point. Suggestions only — the business decides what its
    /// own document declares, and nothing here is applied unless they choose it.
    /// </summary>
    public IReadOnlyList<string> SuggestedHeaderDeclarations { get; init; } = Array.Empty<string>();
    public string SuggestedFooterDeclaration { get; init; } = string.Empty;
}

public record SaveTradeProfileRequest
{
    [MaxLength(40)] public string? IecNumber { get; init; }
    [MaxLength(40)] public string? GstNumber { get; init; }
    [MaxLength(40)] public string? PanNumber { get; init; }
    [MaxLength(60)] public string? ApedaRegistrationNumber { get; init; }
    public DateOnly? ApedaValidUntil { get; init; }

    [MaxLength(200)] public string? PartyNameOverride { get; init; }
    [MaxLength(600)] public string? PartyAddressOverride { get; init; }

    [MaxLength(120)] public string? DefaultCountryOfOrigin { get; init; }
    [MaxLength(300)] public string? DefaultTermsOfDelivery { get; init; }
    [MaxLength(300)] public string? DefaultTermsOfPayment { get; init; }
    [MaxLength(40)] public string? DefaultPricingTerm { get; init; }
    [MaxLength(160)] public string? DefaultPortOfLoading { get; init; }
    [MaxLength(120)] public string? DefaultPreCarriageBy { get; init; }

    [MaxLength(2000)] public string? DefaultHeaderDeclarations { get; init; }
    [MaxLength(2000)] public string? DefaultFooterDeclaration { get; init; }
    [MaxLength(200)] public string? DefaultAuthorisedSignatory { get; init; }
    [MaxLength(3)] public string? DefaultCurrency { get; init; }
}
