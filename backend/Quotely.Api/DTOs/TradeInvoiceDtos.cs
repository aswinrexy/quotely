using System.ComponentModel.DataAnnotations;
using Quotely.Api.Models;

namespace Quotely.Api.DTOs;

// ---------------------------------------------------------------------------
// Reading
// ---------------------------------------------------------------------------

public record TradeLineDto
{
    public Guid Id { get; init; }
    public string? MarksAndNumbers { get; init; }
    /// <summary>Description of goods — the invoice line's own name.</summary>
    public string Description { get; init; } = string.Empty;
    public string? Detail { get; init; }
    public string? Dimension { get; init; }
    public string? HsCode { get; init; }
    public decimal? NetWeight { get; init; }
    public decimal? GrossWeight { get; init; }
    public decimal Quantity { get; init; }
    public string? QuantityUnit { get; init; }
    public decimal Rate { get; init; }
    public string RateBasis { get; init; } = nameof(TradeRateBasis.PerQuantityUnit);
    public string? RateLabel { get; init; }
    public decimal Discount { get; init; }
    public decimal TaxRate { get; init; }
    /// <summary>Server-computed. Never echoed back from the client.</summary>
    public decimal LineTotal { get; init; }
}

public record TradeInvoiceListItemDto
{
    public Guid Id { get; init; }
    /// <summary>Quotely's own number — INV-000001 — which is always present.</summary>
    public string InvoiceNumber { get; init; } = string.Empty;
    /// <summary>The exporter's own series, when they use one. Falls back to the Quotely number.</summary>
    public string DocumentNumber { get; init; } = string.Empty;
    public string TradeType { get; init; } = nameof(Models.TradeType.Export);
    public string DocumentType { get; init; } = nameof(TradeDocumentType.ProformaInvoice);
    public string ConsigneeName { get; init; } = string.Empty;
    public string? FinalDestination { get; init; }
    public DateOnly InvoiceDate { get; init; }
    public string Status { get; init; } = nameof(InvoiceStatus.Draft);
    public string Currency { get; init; } = "INR";
    public decimal GrandTotal { get; init; }
    public decimal TotalPackages { get; init; }
    public decimal TotalNetWeight { get; init; }
    /// <summary>A proforma is never payable; the list says so rather than implying it.</summary>
    public bool IsPayable { get; init; }
}

public record TradeInvoiceDto
{
    public Guid Id { get; init; }
    public string InvoiceNumber { get; init; } = string.Empty;
    public string? DocumentNumber { get; init; }
    public string TradeType { get; init; } = nameof(Models.TradeType.Export);
    public string DocumentType { get; init; } = nameof(TradeDocumentType.ProformaInvoice);

    public DateOnly InvoiceDate { get; init; }
    public DateOnly DueDate { get; init; }
    public string Status { get; init; } = nameof(InvoiceStatus.Draft);
    public bool CanEdit { get; init; }
    public bool CanEditItems { get; init; }
    public bool CanDelete { get; init; }
    /// <summary>
    /// Whether this document may ever carry a Pay button. False for every proforma, regardless of
    /// status — the UI reads this rather than deciding for itself.
    /// </summary>
    public bool IsPayable { get; init; }

    /// <summary>The customer this document belongs to, for navigation. The printed consignee is
    /// the snapshot below, not this.</summary>
    public Guid CustomerId { get; init; }

    public string? BuyerOrderNumber { get; init; }
    public DateOnly? BuyerOrderDate { get; init; }
    public string? OtherReferences { get; init; }

    public string PartyName { get; init; } = string.Empty;
    public string? PartyAddress { get; init; }
    /// <summary>True when the issuing business is also the party the goods ship from.</summary>
    public bool ConsignorSameAsParty { get; init; }
    public string? ConsignorName { get; init; }
    public string? ConsignorAddress { get; init; }
    public string ConsigneeName { get; init; } = string.Empty;
    public string? ConsigneeAddress { get; init; }
    public bool BuyerSameAsConsignee { get; init; }
    public string? BuyerName { get; init; }
    public string? BuyerAddress { get; init; }
    public string? NotifyPartyName { get; init; }
    public string? NotifyPartyAddress { get; init; }

    public string? PreCarriageBy { get; init; }
    public string? PlaceOfReceipt { get; init; }
    public string? VesselOrFlightNumber { get; init; }
    public string? PortOfLoading { get; init; }
    public string? PortOfDischarge { get; init; }
    public string? FinalDestination { get; init; }
    public string? CountryOfOrigin { get; init; }
    public string? CountryOfFinalDestination { get; init; }

    public string? TermsOfDelivery { get; init; }
    public string? TermsOfPayment { get; init; }
    public string? PricingTerm { get; init; }

    public string? IecNumber { get; init; }
    public string? GstNumber { get; init; }
    public string? PanNumber { get; init; }
    public string? ApedaRegistrationNumber { get; init; }
    public DateOnly? ApedaValidUntil { get; init; }

    public string? HeaderDeclarations { get; init; }
    public string? FooterDeclaration { get; init; }
    public string? AuthorisedSignatory { get; init; }

    public string Currency { get; init; } = "INR";
    public decimal Subtotal { get; init; }
    public decimal DiscountTotal { get; init; }
    public decimal TaxTotal { get; init; }
    public decimal GrandTotal { get; init; }
    public decimal TotalNetWeight { get; init; }
    public decimal TotalGrossWeight { get; init; }
    public decimal TotalPackages { get; init; }
    public string WeightUnit { get; init; } = "KGS";
    /// <summary>Computed server-side so the document and its PDF can never disagree.</summary>
    public string AmountInWords { get; init; } = string.Empty;

    public bool HasPublicLink { get; init; }
    public string? Notes { get; init; }

    public BusinessProfileDto? Business { get; init; }
    public IReadOnlyList<TradeLineDto> Items { get; init; } = Array.Empty<TradeLineDto>();
}

// ---------------------------------------------------------------------------
// Writing
// ---------------------------------------------------------------------------

public record SaveTradeLineRequest
{
    [MaxLength(120)] public string? MarksAndNumbers { get; init; }

    [Required, MaxLength(200)]
    public string Description { get; init; } = string.Empty;

    [MaxLength(1000)] public string? Detail { get; init; }
    [MaxLength(120)] public string? Dimension { get; init; }
    [MaxLength(30)] public string? HsCode { get; init; }

    [Range(0, 99_999_999)] public decimal? NetWeight { get; init; }
    [Range(0, 99_999_999)] public decimal? GrossWeight { get; init; }

    [Range(0.0001, 1_000_000)]
    public decimal Quantity { get; init; }

    [MaxLength(40)] public string? QuantityUnit { get; init; }

    [Range(0, 999_999_999)]
    public decimal Rate { get; init; }

    /// <summary>PerQuantityUnit or PerNetWeight. Defaults to PerQuantityUnit.</summary>
    public string? RateBasis { get; init; }

    [MaxLength(80)] public string? RateLabel { get; init; }

    [Range(0, 999_999_999)] public decimal Discount { get; init; }
    [Range(0, 100)] public decimal TaxRate { get; init; }
}

/// <summary>
/// Everything a trade document needs, for both create and update.
///
/// One request shape rather than two: the fields are identical, and a separate create request that
/// drifts from its update twin is how a field ends up editable in one path and not the other.
/// </summary>
public record SaveTradeInvoiceRequest
{
    /// <summary>Must be one of the caller's own customers; anything else is a 404.</summary>
    [Required]
    public Guid CustomerId { get; init; }

    /// <summary>Export or Import.</summary>
    public string? TradeType { get; init; }

    /// <summary>ProformaInvoice or CommercialInvoice.</summary>
    public string? DocumentType { get; init; }

    [Required] public DateOnly InvoiceDate { get; init; }
    public DateOnly? DueDate { get; init; }

    /// <summary>One of: Draft, Sent, PartiallyPaid, Paid, Overdue, Cancelled. Update only.</summary>
    public string? Status { get; init; }

    [MaxLength(60)] public string? DocumentNumber { get; init; }
    [MaxLength(60)] public string? BuyerOrderNumber { get; init; }
    public DateOnly? BuyerOrderDate { get; init; }
    [MaxLength(400)] public string? OtherReferences { get; init; }

    /// <summary>Null falls back to the trade profile, then the business profile.</summary>
    [MaxLength(200)] public string? PartyName { get; init; }
    [MaxLength(600)] public string? PartyAddress { get; init; }

    /// <summary>
    /// False when the goods ship from a party other than the issuing business — an agency
    /// arranging a client's shipment, or a merchant exporter shipping from a manufacturer.
    /// </summary>
    public bool ConsignorSameAsParty { get; init; } = true;
    [MaxLength(200)] public string? ConsignorName { get; init; }
    [MaxLength(600)] public string? ConsignorAddress { get; init; }

    /// <summary>Null falls back to the selected customer's name and address.</summary>
    [MaxLength(200)] public string? ConsigneeName { get; init; }
    [MaxLength(600)] public string? ConsigneeAddress { get; init; }

    public bool BuyerSameAsConsignee { get; init; } = true;
    [MaxLength(200)] public string? BuyerName { get; init; }
    [MaxLength(600)] public string? BuyerAddress { get; init; }

    [MaxLength(200)] public string? NotifyPartyName { get; init; }
    [MaxLength(600)] public string? NotifyPartyAddress { get; init; }

    [MaxLength(120)] public string? PreCarriageBy { get; init; }
    [MaxLength(160)] public string? PlaceOfReceipt { get; init; }
    [MaxLength(120)] public string? VesselOrFlightNumber { get; init; }
    [MaxLength(160)] public string? PortOfLoading { get; init; }
    [MaxLength(160)] public string? PortOfDischarge { get; init; }
    [MaxLength(160)] public string? FinalDestination { get; init; }
    [MaxLength(120)] public string? CountryOfOrigin { get; init; }
    [MaxLength(120)] public string? CountryOfFinalDestination { get; init; }

    [MaxLength(300)] public string? TermsOfDelivery { get; init; }
    [MaxLength(300)] public string? TermsOfPayment { get; init; }
    [MaxLength(40)] public string? PricingTerm { get; init; }

    [MaxLength(40)] public string? IecNumber { get; init; }
    [MaxLength(40)] public string? GstNumber { get; init; }
    [MaxLength(40)] public string? PanNumber { get; init; }
    [MaxLength(60)] public string? ApedaRegistrationNumber { get; init; }
    public DateOnly? ApedaValidUntil { get; init; }

    [MaxLength(2000)] public string? HeaderDeclarations { get; init; }
    [MaxLength(2000)] public string? FooterDeclaration { get; init; }
    [MaxLength(200)] public string? AuthorisedSignatory { get; init; }

    [MaxLength(20)] public string? WeightUnit { get; init; }
    [MaxLength(3)] public string? Currency { get; init; }
    [MaxLength(2000)] public string? Notes { get; init; }

    [Required, MinLength(1)]
    public List<SaveTradeLineRequest> Items { get; init; } = new();
}

/// <summary>
/// The defaults a new trade document starts from, so the browser can render a filled-in form
/// without the user having opened Settings first. Read-only — nothing here is a saved document.
/// </summary>
public record TradeInvoiceDefaultsDto
{
    public string? PartyName { get; init; }
    public string? PartyAddress { get; init; }
    public string? CountryOfOrigin { get; init; }
    public string? TermsOfDelivery { get; init; }
    public string? TermsOfPayment { get; init; }
    public string? PricingTerm { get; init; }
    public string? PortOfLoading { get; init; }
    public string? PreCarriageBy { get; init; }
    public string? IecNumber { get; init; }
    public string? GstNumber { get; init; }
    public string? PanNumber { get; init; }
    public string? ApedaRegistrationNumber { get; init; }
    public DateOnly? ApedaValidUntil { get; init; }
    public string? HeaderDeclarations { get; init; }
    public string? FooterDeclaration { get; init; }
    public string? AuthorisedSignatory { get; init; }
    public string Currency { get; init; } = "INR";
    /// <summary>The next number Quotely would allocate, shown so the user can see it coming.</summary>
    public string SuggestedDocumentNumber { get; init; } = string.Empty;
}
