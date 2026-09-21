namespace Quotely.Api.Models;

/// <summary>
/// The trade detail that turns an ordinary invoice into an import/export document.
///
/// A separate table rather than thirty nullable columns on Invoice, and a 1:1 optional
/// relationship rather than a second invoice entity. That shape is the whole architectural point:
/// an import/export invoice IS an Invoice. It keeps the same number sequence, the same customer
/// snapshot, the same totals, the same public link, the same payment history and the same tenant
/// isolation. Only the extra detail and the printed layout differ, so only the extra detail is new.
///
/// Everything here is a SNAPSHOT, consistent with the rest of the invoice: the exporter block is
/// copied in when the document is raised, so editing the business profile or the trade profile
/// later cannot restate a document a customs broker is already holding.
///
/// Field names follow the reference proforma's own vocabulary where the trade has settled on a
/// term ("Notify Party", "Pre-carriage by", "Port of Discharge"), because that is what the people
/// filling this in say out loud. Where the term is direction-specific, the UI relabels it and the
/// storage stays neutral — see <see cref="TradeType"/>.
/// </summary>
public class TradeInvoiceDetails
{
    public Guid Id { get; set; }

    /// <summary>The invoice this detail belongs to. One row per invoice, enforced by a unique index.</summary>
    public Guid InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }

    public TradeType TradeType { get; set; } = TradeType.Export;
    public TradeDocumentType DocumentType { get; set; } = TradeDocumentType.ProformaInvoice;

    // ---- document references -------------------------------------------
    // The invoice keeps its own Quotely number (INV-000001) for internal identity. This is the
    // number the exporter's own paperwork uses — "NAFPCL/103/26-27" in the reference — and it is
    // what prints. Two numbers because they answer different questions: ours has to be unique and
    // sequential, theirs has to match the series their bank and buyer already know.
    public string? DocumentNumber { get; set; }
    public string? BuyerOrderNumber { get; set; }
    public DateOnly? BuyerOrderDate { get; set; }
    public string? OtherReferences { get; set; }

    // ---- exporter / importer (the issuing business, snapshotted) --------
    public string PartyName { get; set; } = string.Empty;
    public string? PartyAddress { get; set; }

    // ---- consignor, when the goods leave from someone else --------------
    /// <summary>
    /// True — the overwhelming default — means the business named above is also the party the
    /// goods physically ship from, and the document prints one block for both roles.
    ///
    /// It is false for a business that arranges shipments on someone else's behalf: a freight
    /// agency or a merchant exporter whose client's goods leave the client's own premises. There
    /// the exporter of record and the consignor are genuinely different parties, and a document
    /// that named only one of them would misdescribe the shipment.
    ///
    /// Stored rather than inferred from whether the fields below are filled, so that clearing a
    /// consignor is an explicit decision and not an accident of an empty box.
    /// </summary>
    public bool ConsignorSameAsParty { get; set; } = true;
    public string? ConsignorName { get; set; }
    public string? ConsignorAddress { get; set; }

    // ---- consignee -----------------------------------------------------
    public string ConsigneeName { get; set; } = string.Empty;
    public string? ConsigneeAddress { get; set; }

    // ---- buyer, when not the consignee ---------------------------------
    /// <summary>
    /// True prints the reference's "Buyer (if other than Consignee) — Same as Consignee" rather
    /// than repeating the address. Stored rather than inferred by comparing strings, because two
    /// addresses that happen to match are not the same claim as "the buyer IS the consignee".
    /// </summary>
    public bool BuyerSameAsConsignee { get; set; } = true;
    public string? BuyerName { get; set; }
    public string? BuyerAddress { get; set; }

    // ---- notify party --------------------------------------------------
    public string? NotifyPartyName { get; set; }
    public string? NotifyPartyAddress { get; set; }

    // ---- logistics -----------------------------------------------------
    public string? PreCarriageBy { get; set; }
    public string? PlaceOfReceipt { get; set; }
    public string? VesselOrFlightNumber { get; set; }
    public string? PortOfLoading { get; set; }
    public string? PortOfDischarge { get; set; }
    public string? FinalDestination { get; set; }
    public string? CountryOfOrigin { get; set; }
    public string? CountryOfFinalDestination { get; set; }

    // ---- commercial terms ----------------------------------------------
    public string? TermsOfDelivery { get; set; }
    public string? TermsOfPayment { get; set; }

    /// <summary>
    /// The pricing basis printed against the rate and total columns — "C&amp;F", "FOB", "CIF".
    /// Free text, not an enum: Incoterms are revised periodically and an exporter writing a term
    /// this build has never heard of should not be told they are wrong by a dropdown.
    /// </summary>
    public string? PricingTerm { get; set; }

    // ---- regulatory identifiers (snapshotted from the trade profile) ----
    public string? IecNumber { get; set; }
    public string? GstNumber { get; set; }
    public string? PanNumber { get; set; }
    public string? ApedaRegistrationNumber { get; set; }
    public DateOnly? ApedaValidUntil { get; set; }

    // ---- document text --------------------------------------------------
    /// <summary>
    /// The declarations that print above and below the goods table, newline-separated and supplied
    /// entirely by the business.
    ///
    /// Deliberately free text with no built-in legal meaning. The reference carries an IGST
    /// notification reference that is true for that exporter on that shipment and is not true for
    /// everyone — Quotely offers the wording as a starting template and stores whatever the
    /// business decides to say. It is their declaration, made by them.
    /// </summary>
    public string? HeaderDeclarations { get; set; }
    public string? FooterDeclaration { get; set; }

    public string? AuthorisedSignatory { get; set; }

    // ---- server-computed goods totals ------------------------------------
    // Stored, not derived on read, for the same reason the invoice stores its money totals: the
    // document has to keep printing what it printed the day it was issued.
    public decimal TotalNetWeight { get; set; }
    public decimal TotalGrossWeight { get; set; }
    public decimal TotalPackages { get; set; }
    /// <summary>The unit the weight totals are expressed in. KGS in the reference.</summary>
    public string WeightUnit { get; set; } = "KGS";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Whether a document of this kind may carry a Pay button at all.
    ///
    /// A proforma invoice is an offer describing a shipment that has not happened yet; nothing is
    /// owed on it, and putting "Pay Now" on one invites a customer to pay against a document their
    /// bank will not recognise. A commercial invoice is the demand that follows and behaves like
    /// every other Quotely invoice.
    ///
    /// This only withholds payment — it never grants it. The invoice's own status rules still
    /// decide whether an otherwise-payable document is actually collectable.
    /// </summary>
    public bool DefaultPayable => DocumentType == TradeDocumentType.CommercialInvoice;
}
