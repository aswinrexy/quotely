namespace Quotely.Api.Models;

/// <summary>
/// The trade attributes of one goods line, hanging off the ordinary <see cref="InvoiceItem"/>.
///
/// A companion table rather than nine nullable columns on InvoiceItem, and rather than a parallel
/// TradeInvoiceItem table. The reasoning is worth stating, because both alternatives look
/// reasonable until you follow them through:
///
/// A parallel line table would mean the money on a trade invoice lives somewhere the invoice
/// calculator, the payment ledger, the receivables report and the public invoice page have never
/// heard of. Every one of those would need a second code path, and the first bug in any of them
/// would be a trade invoice that is payable for the wrong amount.
///
/// Nine nullable columns on InvoiceItem would work, but they would sit on every domestic line
/// forever, meaning nothing.
///
/// So the InvoiceItem stays the money-bearing line — quantity, rate, line total, exactly as the
/// existing calculator already computes them — and the shipping facts that no domestic invoice has
/// live here, structured and queryable, next to it.
/// </summary>
public class TradeLineDetails
{
    public Guid Id { get; set; }

    /// <summary>The line this detail belongs to. One row per item, enforced by a unique index.</summary>
    public Guid InvoiceItemId { get; set; }
    public InvoiceItem? InvoiceItem { get; set; }

    /// <summary>Shipping marks. "1-", "2" in the reference; often a case-mark string.</summary>
    public string? MarksAndNumbers { get; set; }

    /// <summary>Carton dimensions as written, e.g. "42X32X13". Free text: no agreed unit or order.</summary>
    public string? Dimension { get; set; }

    /// <summary>
    /// Harmonised System tariff code. Stored as text, never as a number: codes are fixed-width and
    /// may carry leading zeros that an integer would silently eat. Not validated against any tariff
    /// schedule — Quotely records what the business declares, it does not classify goods.
    /// </summary>
    public string? HsCode { get; set; }

    public decimal? NetWeight { get; set; }
    public decimal? GrossWeight { get; set; }

    /// <summary>
    /// What the quantity counts — BOXES, BAGS, NOS, and whatever else a business types. Free text
    /// with suggestions in the UI rather than an enum, because the list of things goods are packed
    /// in is not closed and a rejected value helps nobody.
    /// </summary>
    public string? QuantityUnit { get; set; }

    /// <summary>
    /// What the rate is multiplied by. See <see cref="TradeRateBasis"/> — this is the field that
    /// keeps a "Rate/Kg" column from silently multiplying by the wrong number.
    /// </summary>
    public TradeRateBasis RateBasis { get; set; } = TradeRateBasis.PerQuantityUnit;

    /// <summary>
    /// The label printed in the rate column, e.g. "Rate/Kg C&amp;F INR". Kept separate from
    /// <see cref="RateBasis"/> on purpose: in the reference these two disagree, and the document
    /// has to be reproducible exactly as the exporter writes it while the arithmetic stays correct.
    /// </summary>
    public string? RateLabel { get; set; }
}
