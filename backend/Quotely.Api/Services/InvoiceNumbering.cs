using Microsoft.EntityFrameworkCore;
using Quotely.Api.Data;
using Quotely.Api.Models;

namespace Quotely.Api.Services;

/// <summary>
/// Allocating the next invoice number, in one place.
///
/// ONE sequence for every invoice a business raises — converted from a quotation, raised directly,
/// or an import/export document. A trade invoice is not a separate series: it is an invoice, and
/// giving it its own counter would mean two documents could carry the same number in the same
/// books, which is exactly the thing an invoice number exists to prevent.
///
/// Extracted rather than copied, because the copy is where the drift starts: a second
/// implementation that forgets the transaction produces a duplicate number under load, and it
/// produces it rarely enough to reach production.
/// </summary>
public static class InvoiceNumbering
{
    public static string Format(int sequence) => $"INV-{sequence:D6}";

    /// <summary>The number this business would get next. For previews — not a reservation.</summary>
    public static async Task<int> PeekNextSequenceAsync(AppDbContext db, Guid userId, CancellationToken ct)
    {
        var last = await db.Invoices.AsNoTracking()
            .Where(i => i.UserId == userId)
            .OrderByDescending(i => i.Sequence)
            .Select(i => (int?)i.Sequence)
            .FirstOrDefaultAsync(ct);
        return (last ?? 0) + 1;
    }

    /// <summary>
    /// Numbers the invoice and writes it inside one transaction, so a failure anywhere leaves
    /// neither a gap in the sequence nor a half-built document.
    /// </summary>
    public static async Task NumberAndInsertAsync(
        AppDbContext db, Guid userId, Invoice invoice, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            var sequence = await PeekNextSequenceAsync(db, userId, ct);
            invoice.Sequence = sequence;
            invoice.InvoiceNumber = Format(sequence);

            db.Invoices.Add(invoice);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        });
    }
}
