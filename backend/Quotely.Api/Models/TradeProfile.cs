namespace Quotely.Api.Models;

/// <summary>
/// A business's import/export settings, entered once.
///
/// A vegetable exporter sends the same IEC, GST, PAN and APEDA registration on every document of
/// their working life. Asking them to retype it per invoice is how a product loses to the Excel
/// template it is trying to replace, and it is also how a digit gets transposed on the one document
/// that matters.
///
/// Separate from <see cref="BusinessProfile"/> rather than bolted onto it: the vast majority of
/// Quotely businesses never export anything, and their profile should not grow a row of empty
/// customs fields to serve the ones that do. Created lazily, on first read.
///
/// These are DEFAULTS. Everything here is copied onto a document when it is raised and may be
/// overridden on that document, and once copied the document keeps its own values — see
/// <see cref="TradeInvoiceDetails"/>.
/// </summary>
public class TradeProfile
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public AppUser? User { get; set; }

    // ---- regulatory identifiers ----------------------------------------
    // Stored exactly as typed. Quotely does not verify any of these against any register: it is a
    // document builder, and a format check that passed would imply a validity it cannot know.
    public string? IecNumber { get; set; }
    public string? GstNumber { get; set; }
    public string? PanNumber { get; set; }
    public string? ApedaRegistrationNumber { get; set; }
    public DateOnly? ApedaValidUntil { get; set; }

    // ---- document defaults ----------------------------------------------
    /// <summary>
    /// The exporter/importer block as it should print, when it differs from the business profile's
    /// address. Null means "use the business profile", which is the case for most businesses and
    /// avoids a second address to keep in step.
    /// </summary>
    public string? PartyNameOverride { get; set; }
    public string? PartyAddressOverride { get; set; }

    public string? DefaultCountryOfOrigin { get; set; }
    public string? DefaultTermsOfDelivery { get; set; }
    public string? DefaultTermsOfPayment { get; set; }
    public string? DefaultPricingTerm { get; set; }
    public string? DefaultPortOfLoading { get; set; }
    public string? DefaultPreCarriageBy { get; set; }

    /// <summary>Newline-separated. Prints above the document body. Entirely the business's words.</summary>
    public string? DefaultHeaderDeclarations { get; set; }
    public string? DefaultFooterDeclaration { get; set; }
    public string? DefaultAuthorisedSignatory { get; set; }

    /// <summary>Currency new trade documents start in. Trade is often invoiced in USD or AED
    /// while the same business bills domestically in INR, so this is its own setting.</summary>
    public string? DefaultCurrency { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The wording Quotely offers as a starting point, shown in the UI as editable suggestions and
    /// never applied on its own.
    ///
    /// The IGST lines are the ones that appear on the reference document. They are true for that
    /// exporter, for that shipment, under a notification that has a date on it — none of which
    /// Quotely can know for anyone else. They are offered as text an exporter may recognise and
    /// adapt, not as advice, and nothing writes them to a document unless the business chooses them.
    /// </summary>
    public static readonly IReadOnlyList<string> SuggestedHeaderDeclarations = new[]
    {
        "SUPPLY MEANT FOR EXPORT ON PAYMENT OF INTEGRATED TAX (IGST)",
        "SUPPLY MEANT FOR EXPORT UNDER BOND OR LETTER OF UNDERTAKING WITHOUT PAYMENT OF INTEGRATED TAX (IGST)",
    };

    public const string SuggestedFooterDeclaration =
        "We declare that this invoice shows the actual price of the goods described "
        + "and that all particulars are true and correct.";
}
