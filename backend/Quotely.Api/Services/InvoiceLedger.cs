using Microsoft.EntityFrameworkCore;
using Quotely.Api.Data;
using Quotely.Api.DTOs;
using Quotely.Api.Models;

namespace Quotely.Api.Services;

/// <summary>One invoice as a listing needs it, with the paid total the database computed.</summary>
public record InvoiceListRow
{
    public Guid Id { get; init; }
    public string InvoiceNumber { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
    public DateOnly InvoiceDate { get; init; }
    public DateOnly DueDate { get; init; }
    public InvoiceStatus Status { get; init; }
    public decimal GrandTotal { get; init; }
    public string Currency { get; init; } = "INR";
    public decimal Paid { get; init; }
    /// <summary>Null for a directly raised invoice, which has no source quotation.</summary>
    public string? QuotationNumber { get; init; }
}

/// <summary>
/// The database-side definition of what an invoice is owed, and of what "overdue" means.
///
/// V2.3 established that the paid amount is never a stored, editable column — it is summed from
/// payments whenever it is needed. V2.5 adds three more places that ask the same question ("how
/// much is outstanding across every invoice?", "which invoices are overdue?", "what does this
/// customer owe?"), and none of them may grow their own arithmetic. They all compose from here.
///
/// ─────────────────────────────────────────────────────────────────────────────────────────
/// THE RULE: a payment counts toward an invoice when it is Captured and has not been voided.
///
/// It appears below in three mechanical forms — a filter, a scalar projection, and an object
/// projection — because EF Core needs the subquery written inline at each point and cannot inline
/// a shared Expression. They are kept adjacent deliberately: change one, change all three. The
/// in-memory twin of the same rule is <see cref="Payment.CountsTowardPaid"/>.
/// ─────────────────────────────────────────────────────────────────────────────────────────
///
/// Everything here composes as <see cref="IQueryable{T}"/> so the sums and filters run in SQL. The
/// dashboard totals and the overdue filter never page rows into memory to add them up.
/// </summary>
public static class InvoiceLedger
{
    /// <summary>
    /// Invoices that represent real money. A draft was never issued and a cancelled invoice is
    /// void, so neither belongs in anything describing what is owed or collected.
    /// </summary>
    public static IQueryable<Invoice> Receivable(this IQueryable<Invoice> invoices) =>
        invoices.Where(i => i.Status != InvoiceStatus.Draft && i.Status != InvoiceStatus.Cancelled);

    /// <summary>Issued invoices with money still to collect. THE RULE, as a filter.</summary>
    public static IQueryable<Invoice> Unpaid(this IQueryable<Invoice> invoices) =>
        invoices.Receivable().Where(i =>
            i.GrandTotal > (i.Payments
                .Where(p => p.Status == PaymentStatus.Captured && p.VoidedAt == null)
                .Sum(p => (decimal?)p.Amount) ?? 0m));

    /// <summary>
    /// Overdue, derived rather than stored: issued, not cancelled, past its due date, and still
    /// owing something. Deriving it means an invoice becomes overdue because the date rolled over
    /// — no scheduler, no nightly sweep, and no stored status that can drift out of step with the
    /// calendar. Paying an overdue invoice in full stops it being overdue on the very next read.
    /// </summary>
    public static IQueryable<Invoice> Overdue(this IQueryable<Invoice> invoices, DateOnly today) =>
        invoices.Unpaid().Where(i => i.DueDate < today);

    /// <summary>
    /// Each invoice's outstanding amount, for SUM in the database. THE RULE, as a scalar
    /// projection. Clamping is deliberately not applied: an overpaid invoice would contribute a
    /// negative here, and callers work from <see cref="Unpaid"/> where that cannot arise.
    /// </summary>
    public static IQueryable<decimal> OutstandingAmounts(this IQueryable<Invoice> invoices) =>
        invoices.Select(i =>
            i.GrandTotal - (i.Payments
                .Where(p => p.Status == PaymentStatus.Captured && p.VoidedAt == null)
                .Sum(p => (decimal?)p.Amount) ?? 0m));

    /// <summary>
    /// Each invoice's paid total, for SUM in the database. THE RULE, as a scalar projection.
    /// </summary>
    public static IQueryable<decimal> PaidAmounts(this IQueryable<Invoice> invoices) =>
        invoices.Select(i =>
            i.Payments
                .Where(p => p.Status == PaymentStatus.Captured && p.VoidedAt == null)
                .Sum(p => (decimal?)p.Amount) ?? 0m);

    /// <summary>
    /// The paid total for one invoice. THE RULE, applied to a single row — used wherever a service
    /// needs the figure directly rather than as part of a larger query.
    /// </summary>
    public static async Task<decimal> PaidTotalAsync(AppDbContext db, Guid invoiceId, CancellationToken ct) =>
        await db.Payments.AsNoTracking()
            .Where(p => p.InvoiceId == invoiceId
                        && p.Status == PaymentStatus.Captured
                        && p.VoidedAt == null)
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;

    /// <summary>
    /// A listing row with its balance, read in one pass. THE RULE, as a row projection.
    ///
    /// One projection rather than a balance wrapper plus a second Select, because EF resolves the
    /// optional Quotation navigation only while it is still projecting directly from the invoice —
    /// going through an intermediate projection silently yields null, which is how a converted
    /// invoice once lost the quotation number it was raised from.
    /// </summary>
    public static IQueryable<InvoiceListRow> ListRows(this IQueryable<Invoice> invoices) =>
        invoices.Select(i => new InvoiceListRow
        {
            Id = i.Id,
            InvoiceNumber = i.InvoiceNumber,
            CustomerName = i.CustomerName,
            InvoiceDate = i.InvoiceDate,
            DueDate = i.DueDate,
            Status = i.Status,
            GrandTotal = i.GrandTotal,
            Currency = i.Currency,
            Paid = i.Payments
                .Where(p => p.Status == PaymentStatus.Captured && p.VoidedAt == null)
                .Sum(p => (decimal?)p.Amount) ?? 0m,
            QuotationNumber = i.Quotation != null ? i.Quotation.QuotationNumber : null
        });

    /// <summary>Turns a read row into the wire shape, deriving overdue from the balance.</summary>
    public static InvoiceListItemDto ToDto(this InvoiceListRow row, DateOnly today) => new()
    {
        Id = row.Id,
        InvoiceNumber = row.InvoiceNumber,
        CustomerName = row.CustomerName,
        InvoiceDate = row.InvoiceDate,
        DueDate = row.DueDate,
        Status = row.Status.ToString(),
        GrandTotal = row.GrandTotal,
        Currency = row.Currency,
        Paid = row.Paid,
        Outstanding = Math.Max(0m, row.GrandTotal - row.Paid),
        IsOverdue = IsOverdue(row.Status, row.DueDate, row.GrandTotal, row.Paid, today),
        QuotationNumber = row.QuotationNumber ?? string.Empty
    };

    /// <summary>
    /// The same overdue rule for an invoice already in memory with its balance known. Kept beside
    /// the query form so the two cannot drift apart.
    /// </summary>
    public static bool IsOverdue(InvoiceStatus status, DateOnly dueDate, decimal grandTotal, decimal paid, DateOnly today) =>
        status is not (InvoiceStatus.Draft or InvoiceStatus.Cancelled)
        && dueDate < today
        && grandTotal > paid;
}
