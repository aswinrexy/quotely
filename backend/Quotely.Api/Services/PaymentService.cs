using Microsoft.EntityFrameworkCore;
using Quotely.Api.Data;
using Quotely.Api.DTOs;
using Quotely.Api.Middleware;
using Quotely.Api.Models;
using Quotely.Api.Payments;

namespace Quotely.Api.Services;

public interface IPaymentService
{
    /// <summary>Creates, or safely reuses, a provider order for the invoice's outstanding balance.</summary>
    Task<(Payment Payment, ProviderOrder Order)> StartPaymentAsync(Invoice invoice, CancellationToken ct = default);

    /// <summary>
    /// The one path by which a provider outcome becomes money in our database. Both checkout
    /// verification and webhook processing call this and nothing else.
    /// </summary>
    Task<Payment> ProcessOutcomeAsync(PaymentOutcome outcome, CancellationToken ct = default);

    Task<PaymentSummaryDto> GetSummaryAsync(Guid invoiceId, CancellationToken ct = default);
    Task<InvoicePaymentsDto> GetInvoicePaymentsAsync(Guid userId, Guid invoiceId, CancellationToken ct = default);

    /// <summary>Recomputes paid/outstanding from captured payments and moves the invoice status.</summary>
    Task<PaymentSummaryDto> RecalculateInvoiceAsync(Guid invoiceId, CancellationToken ct = default);
}

public class PaymentService : IPaymentService
{
    /// <summary>
    /// How long an unused order is offered back to the same customer. Long enough to cover a
    /// double-click, a refresh, a second tab or a dropped connection; short enough that a stale
    /// order is not resurrected days later.
    /// </summary>
    private static readonly TimeSpan OrderReuseWindow = TimeSpan.FromMinutes(15);

    private readonly AppDbContext _db;
    private readonly IPaymentProvider _provider;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(AppDbContext db, IPaymentProvider provider, ILogger<PaymentService> logger)
    {
        _db = db;
        _provider = provider;
        _logger = logger;
    }

    // ---- starting a payment --------------------------------------------

    public async Task<(Payment Payment, ProviderOrder Order)> StartPaymentAsync(Invoice invoice, CancellationToken ct = default)
    {
        if (!_provider.IsConfigured)
            throw new ApiException(System.Net.HttpStatusCode.ServiceUnavailable,
                "Online payments are not available at the moment. Please contact the business.");

        // The amount is computed here, from records, every time. Nothing the browser sends is
        // consulted: the create-order request has no amount field at all.
        var outstanding = await OutstandingAsync(invoice, ct);

        if (!invoice.AcceptsPayments)
            throw ApiException.Conflict(InvoiceNotPayableMessage(invoice));

        if (outstanding <= 0)
            throw ApiException.Conflict("This invoice has been paid in full.");

        // A live order for the same balance is handed back rather than duplicated. This is what
        // makes a double-click, a refresh and a second tab converge on one payment attempt.
        // Evaluated here rather than inside the expression tree, which EF cannot translate.
        var reuseCutoff = DateTime.UtcNow - OrderReuseWindow;

        var reusable = await _db.Payments
            .Where(p => p.InvoiceId == invoice.Id
                        && p.Status == PaymentStatus.Created
                        && p.ProviderPaymentId == null
                        && p.Amount == outstanding
                        && p.CreatedAt > reuseCutoff)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (reusable is not null)
        {
            _logger.LogInformation(
                "Reusing payment order {ProviderOrderId} for invoice {InvoiceId}",
                reusable.ProviderOrderId, invoice.Id);

            return (reusable, new ProviderOrder(
                reusable.ProviderOrderId, _provider.ToMinorUnits(reusable.Amount), reusable.Currency));
        }

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            InvoiceId = invoice.Id,
            UserId = invoice.UserId,
            Amount = outstanding,
            Currency = invoice.Currency,
            Status = PaymentStatus.Created,
            Provider = _provider.Name,
            CustomerName = invoice.CustomerName,
            CustomerEmail = invoice.CustomerEmail
        };

        ProviderOrder order;
        try
        {
            order = await _provider.CreateOrderAsync(
                new CreateOrderRequest(outstanding, invoice.Currency, invoice.InvoiceNumber, payment.Id), ct);
        }
        catch (PaymentProviderException ex)
        {
            _logger.LogWarning(ex, "Payment order creation failed for invoice {InvoiceId}", invoice.Id);
            throw new ApiException(System.Net.HttpStatusCode.BadGateway, ex.Message);
        }

        payment.ProviderOrderId = order.OrderId;
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Created payment order {ProviderOrderId} for invoice {InvoiceId} ({Amount} {Currency})",
            order.OrderId, invoice.Id, outstanding, invoice.Currency);

        return (payment, order);
    }

    // ---- recording an outcome ------------------------------------------

    /// <summary>
    /// Idempotent by construction. A provider payment id maps to exactly one row, enforced by a
    /// unique index, so two callers racing — a webhook and a checkout callback, say — end with
    /// one payment record and one set of totals.
    /// </summary>
    public async Task<Payment> ProcessOutcomeAsync(PaymentOutcome outcome, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(outcome.ProviderPaymentId))
            throw ApiException.BadRequest("The payment reference was missing.");

        var strategy = _db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            var existing = await _db.Payments
                .FirstOrDefaultAsync(p => p.ProviderPaymentId == outcome.ProviderPaymentId, ct);

            if (existing is not null)
                return await ApplyOutcomeAsync(existing, outcome, ct);

            // No record of this payment yet: attach it to the order we created for it. Claiming
            // an unused row keeps one attempt per order; a retry on the same order gets its own.
            var order = await _db.Payments
                .Where(p => p.ProviderOrderId == outcome.ProviderOrderId)
                .OrderBy(p => p.CreatedAt)
                .ToListAsync(ct);

            if (order.Count == 0)
            {
                // An order we never created cannot be credited to any invoice of ours.
                _logger.LogWarning(
                    "Ignoring payment {ProviderPaymentId} for unknown order {ProviderOrderId}",
                    outcome.ProviderPaymentId, outcome.ProviderOrderId);
                throw ApiException.NotFound("Payment");
            }

            var claimable = order.FirstOrDefault(p => p.ProviderPaymentId is null);
            var template = order[0];

            if (claimable is null)
            {
                // The order was retried after a failure: a second attempt on the same order.
                claimable = new Payment
                {
                    Id = Guid.NewGuid(),
                    InvoiceId = template.InvoiceId,
                    UserId = template.UserId,
                    Amount = template.Amount,
                    Currency = template.Currency,
                    Provider = template.Provider,
                    ProviderOrderId = template.ProviderOrderId,
                    Status = PaymentStatus.Created,
                    CustomerName = template.CustomerName,
                    CustomerEmail = template.CustomerEmail
                };
                _db.Payments.Add(claimable);
            }

            claimable.ProviderPaymentId = outcome.ProviderPaymentId;

            try
            {
                return await ApplyOutcomeAsync(claimable, outcome, ct);
            }
            catch (DbUpdateException)
            {
                // Another caller claimed the same provider payment first. The unique index did
                // its job; re-read and report their result rather than creating a second row.
                _db.ChangeTracker.Clear();
                var winner = await _db.Payments
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.ProviderPaymentId == outcome.ProviderPaymentId, ct);

                if (winner is null) throw;

                _logger.LogInformation(
                    "Payment {ProviderPaymentId} was already recorded by a concurrent request",
                    outcome.ProviderPaymentId);
                return winner;
            }
        });
    }

    /// <summary>
    /// Applies a provider outcome to a payment row and recalculates the invoice. Refuses to walk
    /// a payment backwards: webhook ordering is not guaranteed, so a late payment.authorized
    /// must not demote an already-captured payment.
    /// </summary>
    private async Task<Payment> ApplyOutcomeAsync(Payment payment, PaymentOutcome outcome, CancellationToken ct)
    {
        if (!outcome.Status.SupersedesOrEquals(payment.Status))
        {
            _logger.LogInformation(
                "Ignoring out-of-order {Incoming} for payment {ProviderPaymentId} already {Current}",
                outcome.Status, outcome.ProviderPaymentId, payment.Status);
            return payment;
        }

        // Cross-check what the provider says against the order we registered.
        if (!string.Equals(payment.Currency, outcome.Currency, StringComparison.OrdinalIgnoreCase))
            throw ApiException.BadRequest("The payment currency did not match this invoice.");

        var expected = _provider.ToMinorUnits(payment.Amount);

        // Crediting more than we asked for is the dangerous direction: it is how a forged or
        // misattributed payment would inflate an invoice. Refused outright.
        if (outcome.AmountInMinorUnits > expected)
        {
            _logger.LogWarning(
                "Refused payment {ProviderPaymentId}: provider reported {Actual} against an order for {Expected}",
                outcome.ProviderPaymentId, outcome.AmountInMinorUnits, expected);
            throw ApiException.BadRequest("The payment amount did not match this invoice.");
        }

        // Less is legitimate — a capture can settle for under the authorised amount — and is
        // recorded at its true value, which is what makes a part payment a part payment.
        if (outcome.AmountInMinorUnits > 0 && outcome.AmountInMinorUnits < expected)
        {
            var actual = outcome.AmountInMinorUnits / 100m;
            _logger.LogInformation(
                "Payment {ProviderPaymentId} settled for {Actual} of an expected {Expected} {Currency}",
                outcome.ProviderPaymentId, actual, payment.Amount, payment.Currency);
            payment.Amount = actual;
        }

        var alreadyCaptured = payment.Status == PaymentStatus.Captured;

        payment.Status = outcome.Status;
        payment.Method = outcome.Method ?? payment.Method;
        payment.FailureReason = outcome.Status == PaymentStatus.Failed ? outcome.FailureReason : null;
        payment.ConcurrencyStamp = Guid.NewGuid();

        if (outcome.Status == PaymentStatus.Captured && payment.PaidAt is null)
            payment.PaidAt = outcome.PaidAt ?? DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        if (!alreadyCaptured && outcome.Status == PaymentStatus.Captured)
        {
            _logger.LogInformation(
                "Recorded payment {ProviderPaymentId} of {Amount} {Currency} for invoice {InvoiceId}",
                payment.ProviderPaymentId, payment.Amount, payment.Currency, payment.InvoiceId);
        }

        await RecalculateInvoiceAsync(payment.InvoiceId, ct);
        return payment;
    }

    // ---- financial state ------------------------------------------------

    /// <summary>
    /// The invoice's paid amount is never stored as an editable field: it is the sum of captured
    /// payments, recomputed here and reflected into the status.
    /// </summary>
    public async Task<PaymentSummaryDto> RecalculateInvoiceAsync(Guid invoiceId, CancellationToken ct = default)
    {
        var invoice = await _db.Invoices.FirstOrDefaultAsync(i => i.Id == invoiceId, ct)
                      ?? throw ApiException.NotFound("Invoice");

        var paid = await CapturedTotalAsync(invoiceId, ct);

        // Existing invoices carry a status the owner set by hand, and V2.2's rules still apply.
        // Payments only take over once money has actually arrived, so an invoice nobody has paid
        // keeps whatever status its owner chose.
        if (paid > 0 && invoice.Status != InvoiceStatus.Cancelled)
        {
            var next = paid >= invoice.GrandTotal ? InvoiceStatus.Paid : InvoiceStatus.PartiallyPaid;
            if (invoice.Status != next)
            {
                _logger.LogInformation(
                    "Invoice {InvoiceId} moves {From} → {To} ({Paid} of {Total} {Currency})",
                    invoiceId, invoice.Status, next, paid, invoice.GrandTotal, invoice.Currency);
                invoice.Status = next;
                await _db.SaveChangesAsync(ct);
            }
        }

        if (paid > invoice.GrandTotal)
        {
            // Recorded truthfully — the money is real — but flagged: it needs a human decision.
            _logger.LogWarning(
                "Invoice {InvoiceId} is overpaid: {Paid} captured against a total of {Total} {Currency}",
                invoiceId, paid, invoice.GrandTotal, invoice.Currency);
        }

        return Summarise(invoice, paid, await HasPendingAsync(invoiceId, ct));
    }

    public async Task<PaymentSummaryDto> GetSummaryAsync(Guid invoiceId, CancellationToken ct = default)
    {
        var invoice = await _db.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == invoiceId, ct)
                      ?? throw ApiException.NotFound("Invoice");

        return Summarise(invoice, await CapturedTotalAsync(invoiceId, ct), await HasPendingAsync(invoiceId, ct));
    }

    public async Task<InvoicePaymentsDto> GetInvoicePaymentsAsync(Guid userId, Guid invoiceId, CancellationToken ct = default)
    {
        // Tenant-scoped exactly like every other authenticated invoice query; another owner's
        // invoice is a 404 rather than a 403.
        var invoice = await _db.Invoices.AsNoTracking()
                          .FirstOrDefaultAsync(i => i.Id == invoiceId && i.UserId == userId, ct)
                      ?? throw ApiException.NotFound("Invoice");

        var payments = await _db.Payments.AsNoTracking()
            .Where(p => p.InvoiceId == invoiceId)
            // A "Created" row is an order nobody paid; it is noise in a payment history.
            .Where(p => p.Status != PaymentStatus.Created)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);

        return new InvoicePaymentsDto
        {
            Summary = Summarise(
                invoice,
                payments.Where(p => p.Status == PaymentStatus.Captured).Sum(p => p.Amount),
                payments.Any(p => p.Status == PaymentStatus.Pending)),
            Payments = payments.Select(Map).ToList()
        };
    }

    // ---- helpers --------------------------------------------------------

    /// <summary>The paid amount, always summed from captured rows — never read from a column.</summary>
    private async Task<decimal> CapturedTotalAsync(Guid invoiceId, CancellationToken ct) =>
        await _db.Payments.AsNoTracking()
            .Where(p => p.InvoiceId == invoiceId && p.Status == PaymentStatus.Captured)
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;

    private Task<bool> HasPendingAsync(Guid invoiceId, CancellationToken ct) =>
        _db.Payments.AsNoTracking()
            .AnyAsync(p => p.InvoiceId == invoiceId && p.Status == PaymentStatus.Pending, ct);

    private async Task<decimal> OutstandingAsync(Invoice invoice, CancellationToken ct)
    {
        var paid = await CapturedTotalAsync(invoice.Id, ct);
        return Math.Max(0m, invoice.GrandTotal - paid);
    }

    private static PaymentSummaryDto Summarise(Invoice invoice, decimal paid, bool hasPending)
    {
        // Clamped defensively: a negative outstanding would be nonsense on screen, and the
        // overpayment itself is logged as a warning rather than hidden.
        var outstanding = Math.Max(0m, invoice.GrandTotal - paid);

        return new PaymentSummaryDto
        {
            Total = invoice.GrandTotal,
            Paid = paid,
            Outstanding = outstanding,
            Currency = invoice.Currency,
            InvoiceStatus = invoice.Status.ToString(),
            CanPay = invoice.AcceptsPayments && outstanding > 0,
            HasPendingPayment = hasPending
        };
    }

    internal static string InvoiceNotPayableMessage(Invoice invoice) => invoice.Status switch
    {
        InvoiceStatus.Draft => "This invoice has not been issued yet.",
        InvoiceStatus.Cancelled => "This invoice has been cancelled and cannot be paid.",
        InvoiceStatus.Paid => "This invoice has been paid in full.",
        _ => "This invoice cannot be paid online at the moment."
    };

    private static PaymentDto Map(Payment p) => new()
    {
        Id = p.Id,
        Amount = p.Amount,
        Currency = p.Currency,
        Status = p.Status.ToString(),
        Provider = p.Provider,
        Reference = p.ProviderPaymentId,
        OrderReference = p.ProviderOrderId,
        Method = p.Method,
        FailureReason = p.FailureReason,
        PaidAt = p.PaidAt,
        CreatedAt = p.CreatedAt
    };
}
