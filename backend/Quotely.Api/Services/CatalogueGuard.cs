using Microsoft.EntityFrameworkCore;
using Quotely.Api.Data;
using Quotely.Api.Middleware;

namespace Quotely.Api.Services;

/// <summary>
/// A business must have something to sell before it can bill for it.
///
/// Quotely used to accept a document whose every line was typed from scratch, which meant a new
/// account could invoice for months without its catalogue ever holding a single entry. The product
/// list stopped describing the business, prices drifted line by line, and the same service went
/// out at three different amounts because nothing held the number in one place.
///
/// So a document now starts from the catalogue. This guard is the server half of that rule: it
/// refuses to create an invoice or a quotation for a business with an empty product list.
///
/// WHAT THIS DELIBERATELY DOES NOT DO: it does not require each individual line to reference a
/// catalogue entry. It cannot, and pretending otherwise would be worse than not trying. An
/// <see cref="Models.InvoiceItem"/> carries no ProductId at all — an invoice line is a frozen
/// snapshot, so a later price change can never restate a bill that was already sent. Line-level
/// provenance exists only on quotations, where ProductId is optional by design. The enforceable,
/// honest rule at this layer is the one implemented here; the per-line requirement lives in the
/// forms, where the person is choosing.
/// </summary>
internal static class CatalogueGuard
{
    /// <param name="documentKind">"invoice" or "quotation" — this appears in the message the
    /// person reads, so it is spelled the way they would say it.</param>
    public static async Task EnsureNotEmptyAsync(
        AppDbContext db, Guid userId, string documentKind, CancellationToken ct = default)
    {
        var hasAny = await db.Products.AnyAsync(p => p.UserId == userId, ct);
        if (hasAny) return;

        throw ApiException.BadRequest(
            $"Add at least one product or service before creating {Article(documentKind)} {documentKind}. " +
            "Every line is priced from your catalogue.");
    }

    private static string Article(string word) =>
        word.Length > 0 && "aeiou".Contains(char.ToLowerInvariant(word[0])) ? "an" : "a";
}
