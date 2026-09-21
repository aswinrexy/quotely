namespace Quotely.Api.Models;

/// <summary>
/// What kind of document an invoice is.
///
/// A discriminator rather than a scattering of booleans, and deliberately NOT a separate entity:
/// an import/export invoice is still an Invoice. It carries the same number sequence, the same
/// customer snapshot, the same totals, the same public link and the same payment history. What
/// differs is the extra trade detail hanging off it and how it prints.
///
/// <see cref="Standard"/> is 0 so every invoice that existed before this feature reads correctly
/// without touching a single row.
/// </summary>
public enum InvoiceType
{
    Standard = 0,
    ImportExport = 1
}

/// <summary>Which direction the goods move, from the issuing business's point of view.</summary>
public enum TradeType
{
    /// <summary>The business is shipping goods out. The reference document for this feature.</summary>
    Export = 0,

    /// <summary>The business is bringing goods in. The same document with the parties reversed.</summary>
    Import = 1
}

/// <summary>
/// Proforma or commercial, which is a real distinction rather than a label.
///
/// A proforma invoice is an offer: it states what a shipment WILL contain and what it will cost,
/// and nothing is owed on it. A commercial invoice is the demand for payment that follows. The
/// difference decides whether a Pay button may appear — see <c>TradeInvoiceDetails.DefaultPayable</c>.
/// </summary>
public enum TradeDocumentType
{
    ProformaInvoice = 0,
    CommercialInvoice = 1
}

/// <summary>
/// What the rate on a goods line is quoted against.
///
/// This exists because of a trap in real trade documents, and getting it wrong silently produces
/// the wrong total. The reference proforma prints its rate column as "Rate/Kg" while the figure it
/// actually multiplies is the package count: 132 boxes at 590 gives the printed 77,880, whereas
/// 435.60 kg at 590 would give 257,004. The column heading is the exporter's own wording, not a
/// statement of arithmetic.
///
/// So the basis is stored rather than inferred from a label. <see cref="PerQuantityUnit"/> is the
/// default because it is what the reference does; <see cref="PerNetWeight"/> is there for the
/// exporters who genuinely do price by the kilogram, and for them the label and the maths agree.
/// </summary>
public enum TradeRateBasis
{
    /// <summary>Rate × quantity. A price per box, bag or piece.</summary>
    PerQuantityUnit = 0,

    /// <summary>Rate × net weight. A true price per kilogram.</summary>
    PerNetWeight = 1
}
