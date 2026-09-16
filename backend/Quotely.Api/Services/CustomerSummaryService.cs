using Microsoft.EntityFrameworkCore;
using Quotely.Api.Data;
using Quotely.Api.DTOs;
using Quotely.Api.Middleware;
using Quotely.Api.Models;

namespace Quotely.Api.Services;

public interface ICustomerSummaryService
{
    Task<CustomerSummaryDto> GetAsync(
        Guid userId, Guid customerId, int page = 1, int pageSize = 10, CancellationToken ct = default);
}

/// <summary>
/// One customer's financial standing. Built on <see cref="InvoiceLedger"/> like every other
/// balance in the system, so a customer's outstanding total cannot disagree with the dashboard's
/// or the invoice's own.
/// </summary>
public class CustomerSummaryService : ICustomerSummaryService
{
    private readonly AppDbContext _db;

    public CustomerSummaryService(AppDbContext db) => _db = db;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    public async Task<CustomerSummaryDto> GetAsync(
        Guid userId, Guid customerId, int page = 1, int pageSize = 10, CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        // Ownership first: another tenant's customer is a 404, never a hint that they exist.
        var customer = await _db.Customers.AsNoTracking()
                           .FirstOrDefaultAsync(c => c.Id == customerId && c.UserId == userId, ct)
                       ?? throw ApiException.NotFound("Customer");

        var today = Today;

        // Scoped by tenant AND customer in the database. The page the caller asked for is the
        // page the database produces — the previous customer page filtered page one of every
        // quotation in the browser, which silently lost anything past the first page.
        var invoices = _db.Invoices.AsNoTracking()
            .Where(i => i.UserId == userId && i.CustomerId == customerId);

        var issued = invoices.Receivable();
        var overdue = invoices.Overdue(today);

        // Aggregated in SQL, over every invoice for this customer rather than one page of them.
        var totalInvoiced = await issued.SumAsync(i => (decimal?)i.GrandTotal, ct) ?? 0m;
        var totalPaid = await issued.PaidAmounts().SumAsync(ct);
        var totalOverdue = await overdue.OutstandingAmounts().SumAsync(ct);
        var overdueCount = await overdue.CountAsync(ct);

        // The listing shows every invoice, drafts included, because the owner is looking at their
        // own record of this customer. Only the money totals exclude drafts.
        var total = await invoices.CountAsync(ct);

        var rows = await invoices
            .OrderByDescending(i => i.Sequence)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ListRows()
            .ToListAsync(ct);

        var items = rows.Select(r => r.ToDto(today)).ToList();

        var currency = await _db.BusinessProfiles.AsNoTracking()
            .Where(b => b.UserId == userId)
            .Select(b => b.Currency)
            .FirstOrDefaultAsync(ct) ?? "INR";

        return new CustomerSummaryDto
        {
            CustomerId = customer.Id,
            CustomerName = customer.Name,
            TotalInvoiced = totalInvoiced,
            TotalPaid = totalPaid,
            TotalOutstanding = Math.Max(0m, totalInvoiced - totalPaid),
            TotalOverdue = totalOverdue,
            InvoiceCount = total,
            OverdueCount = overdueCount,
            Currency = currency,
            Invoices = new PagedResult<InvoiceListItemDto>(items, page, pageSize, total)
        };
    }
}
