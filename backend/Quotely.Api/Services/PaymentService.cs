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

    /// <summary>
    /// Records money the business received outside the gateway. The second way into the ledger,
    /// and the only other one: it ends at the same recalculation as a provider outcome does.
    /// </summary>
    Task<PaymentDto> RecordManualPaymentAsync(
        Guid userId, Guid invoiceId, RecordManualPaymentRequest request, CancellationToken ct = default);

    /// <summary>
    /// Stops a manual payment counting toward the invoice balance, without deleting it. Gateway
    /// payments are refused: their record belongs to the provider.
    /// </summary>
    Task<PaymentDto> VoidPaymentAsync(Guid userId, Guid invoiceId, Guid paymentId, CancellationToken ct = default);

    Task<PaymentSummaryDto> GetSummaryAsync(Guid invoiceId, CancellationToken ct = default);
    Task<InvoicePaymentsDto> GetInvoicePaymentsAsync(Guid userId, Guid invoiceId, CancellationToken ct = default);

    /// <summary>Recomputes paid/outstanding from captured payments and moves the invoice status.</summary>
    Task<PaymentSummaryDto> RecalculateInvoiceAsync(Guid invoiceId, CancellationToken ct = default);
}

public class PaymentService : IPaymentService
{
    /// <summary>
    /// How long an unpaid order keeps its reservation. Long enough to cover a double-click, a
    /// refresh, a second tab or a dropped connection; short enough that an abandoned attempt does
    /// not lock the invoice out of being paid.
    /// </summary>
    private static readonly TimeSpan OrderReservationWindow = TimeSpan.FromMinutes(15);

    /// <summary>
    /// An authorised payment is money the provider is holding, so its reservation lasts far
    /// longer than an untouched order's — but not forever, in case a capture never arrives.
    /// </summary>
    private static readonly TimeSpan AuthorisedReservationWindow = TimeSpan.FromHours(24);

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

        if (!invoice.AcceptsPayments)
            throw ApiException.Conflict(InvoiceNotPayableMessage(invoice));

        var strategy = _db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            // Release anything whose reservation has lapsed, so an abandoned tab does not lock
            // this invoice out of being paid.
            await ReleaseExpiredReservationsAsync(invoice.Id, ct);

            var live = await _db.Payments
                .FirstOrDefaultAsync(p => p.ReservationSlot == invoice.Id, ct);

            // The amount is computed here, from records, every time. Nothing the browser sends is
            // consulted: the create-order request has no amount field at all.
            var captured = await CapturedTotalAsync(invoice.Id, ct);
            var outstanding = Math.Max(0m, invoice.GrandTotal - captured);

            if (outstanding <= 0)
                throw ApiException.Conflict("This invoice has been paid in full.");

            if (live is not null)
            {
                // An authorised payment is already collecting this balance. Starting a second one
                // would be how an invoice gets paid twice.
                if (live.Status == PaymentStatus.Pending)
                    throw ApiException.Conflict(
                        "A payment on this invoice is being confirmed. Please wait a moment before trying again.");

                // The live order still matches what is owed: hand the same one back. This is what
                // makes a double-click, a refresh and a second tab converge on one attempt.
                if (live.Amount == outstanding)
                {
                    _logger.LogInformation(
                        "Reusing payment order {ProviderOrderId} for invoice {InvoiceId}",
                        live.ProviderOrderId, invoice.Id);

                    return (live, new ProviderOrder(
                        RequireProviderOrderId(live), _provider.ToMinorUnits(live.Amount), live.Currency));
                }

                // The balance moved under it — a part payment landed — so the stale order is
                // retired and its reservation freed before a correctly priced one is opened.
                _logger.LogInformation(
                    "Retiring payment order {ProviderOrderId}: reserved {Reserved} but {Outstanding} is now owed",
                    live.ProviderOrderId, live.Amount, outstanding);

                live.Status = PaymentStatus.Cancelled;
                live.ReservationSlot = null;
                await _db.SaveChangesAsync(ct);
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
                // Taking the slot is what reserves the balance against a concurrent attempt.
                ReservationSlot = invoice.Id,
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

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Another request took the slot between our read and our write. The unique index
                // caught what an application check could not, and the caller is given the
                // winner's order rather than a second way to pay the same balance. The order we
                // opened at the provider is simply never used; unused orders expire on their own.
                _db.ChangeTracker.Clear();

                var winner = await _db.Payments.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.ReservationSlot == invoice.Id, ct);

                if (winner is null) throw;

                _logger.LogInformation(
                    "Concurrent payment order for invoice {InvoiceId} resolved to {ProviderOrderId}",
                    invoice.Id, winner.ProviderOrderId);

                return (winner, new ProviderOrder(
                    RequireProviderOrderId(winner), _provider.ToMinorUnits(winner.Amount), winner.Currency));
            }

            _logger.LogInformation(
                "Created payment order {ProviderOrderId} for invoice {InvoiceId} ({Amount} {Currency})",
                order.OrderId, invoice.Id, outstanding, invoice.Currency);

            return (payment, order);
        });
    }

    /// <summary>
    /// A reservation is only ever taken by a gateway attempt, which always has a provider order —
    /// manual payments never reserve. ProviderOrderId became nullable in V2.5 for their sake, so
    /// this states the invariant where a reservation is read back rather than assuming it.
    /// </summary>
    private static string RequireProviderOrderId(Payment payment) =>
        payment.ProviderOrderId
        ?? throw new InvalidOperationException(
            $"Payment {payment.Id} holds a reservation but has no provider order id.");

    /// <summary>
    /// Frees reservations that have lapsed. An untouched order holds the balance only briefly;
    /// an authorised payment holds it far longer, because the provider really is holding money.
    /// </summary>
    private async Task ReleaseExpiredReservationsAsync(Guid invoiceId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var createdCutoff = now - OrderReservationWindow;
        var authorisedCutoff = now - AuthorisedReservationWindow;

        var stale = await _db.Payments
            .Where(p => p.ReservationSlot == invoiceId
                        && ((p.Status == PaymentStatus.Created && p.CreatedAt < createdCutoff)
                            || (p.Status == PaymentStatus.Pending && p.CreatedAt < authorisedCutoff)))
            .ToListAsync(ct);

        if (stale.Count == 0) return;

        foreach (var payment in stale)
        {
            // The reservation lapses; the payment row itself is kept as a record of the attempt.
            payment.ReservationSlot = null;
            if (payment.Status == PaymentStatus.Created) payment.Status = PaymentStatus.Cancelled;

            _logger.LogInformation(
                "Released lapsed reservation on payment order {ProviderOrderId} for invoice {InvoiceId}",
                payment.ProviderOrderId, invoiceId);
        }

        await _db.SaveChangesAsync(ct);
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
                    // Not a reservation: this row is being created to record an outcome that has
                    // already happened, not to hold the balance for a future attempt.
                    ReservationSlot = null,
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

        // A settled attempt no longer reserves the balance: captured money moves into the paid
        // total, and a failed or cancelled one frees the invoice to be paid again.
        if (!payment.IsLiveAttempt) payment.ReservationSlot = null;
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

    // ---- manual payments (V2.5) -----------------------------------------

    /// <summary>
    /// Records money received outside the gateway. This is the ledger's second entrance, and it
    /// deliberately shares everything past the front door with the first: the same Payment table,
    /// the same Captured status, and the same <see cref="RecalculateInvoiceAsync"/>. There is no
    /// separate manual balance anywhere in the system.
    ///
    /// The amount the owner types is checked against a balance the server computes from records,
    /// so the browser cannot cause an invoice to be over-collected by sending a larger number.
    /// </summary>
    public async Task<PaymentDto> RecordManualPaymentAsync(
        Guid userId, Guid invoiceId, RecordManualPaymentRequest request, CancellationToken ct = default)
    {
        // Ownership first. Another tenant's invoice is a 404 exactly as an unknown id would be.
        var invoice = await _db.Invoices.FirstOrDefaultAsync(i => i.Id == invoiceId && i.UserId == userId, ct)
                      ?? throw ApiException.NotFound("Invoice");

        if (invoice.Status == InvoiceStatus.Draft)
            throw ApiException.Conflict(
                $"{invoice.InvoiceNumber} has not been issued yet, so it cannot take a payment.");

        if (invoice.Status == InvoiceStatus.Cancelled)
            throw ApiException.Conflict(
                $"{invoice.InvoiceNumber} has been cancelled and cannot take a payment.");

        var method = ManualPaymentMethods.Normalise(request.Method)
                     ?? throw ApiException.BadRequest(
                         $"\"{request.Method}\" is not a payment method. Use one of: {ManualPaymentMethods.Describe()}.");

        if (request.Amount <= 0)
            throw ApiException.BadRequest("A payment amount must be greater than zero.");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var paymentDate = request.PaymentDate ?? today;

        // Back-dating is normal — last week's cash gets entered today. Forward-dating is not:
        // that money has not been received, and recording it would overstate what is collected.
        if (paymentDate > today)
            throw ApiException.BadRequest("A payment date cannot be in the future.");

        var strategy = _db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            // Computed here, from the ledger, at the moment of writing — never taken from the
            // request, and re-read inside the transaction so a gateway payment landing at the
            // same time is already accounted for.
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);

            var paid = await CapturedTotalAsync(invoice.Id, ct);
            var outstanding = Math.Max(0m, invoice.GrandTotal - paid);

            if (outstanding <= 0)
                throw ApiException.BadRequest(
                    $"{invoice.InvoiceNumber} is already paid in full.");

            if (request.Amount > outstanding)
                throw ApiException.BadRequest(
                    $"That is more than the {outstanding:N2} {invoice.Currency} still outstanding on {invoice.InvoiceNumber}.");

            var payment = new Payment
            {
                Id = Guid.NewGuid(),
                InvoiceId = invoice.Id,
                UserId = userId,
                Amount = request.Amount,
                Currency = invoice.Currency,
                // Captured immediately: unlike a gateway payment there is nothing left to confirm.
                // The owner is telling us money they already hold arrived.
                Status = PaymentStatus.Captured,
                Source = PaymentSource.Manual,
                Provider = PaymentProviders.Manual,
                // Genuinely no provider order and no provider payment — not a placeholder.
                ProviderOrderId = null,
                ProviderPaymentId = null,
                Method = method,
                Reference = string.IsNullOrWhiteSpace(request.Reference) ? null : request.Reference.Trim(),
                Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
                // Manual payments never reserve: there is no checkout window to hold open.
                ReservationSlot = null,
                CustomerName = invoice.CustomerName,
                CustomerEmail = invoice.CustomerEmail,
                PaidAt = paymentDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
            };

            _db.Payments.Add(payment);
            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            _logger.LogInformation(
                "Recorded manual {Method} payment of {Amount} {Currency} for invoice {InvoiceId}",
                method, payment.Amount, payment.Currency, invoice.Id);

            // The same recalculation a Razorpay capture triggers. One path, one set of totals.
            await RecalculateInvoiceAsync(invoice.Id, ct);

            return Map(payment);
        });
    }

    /// <summary>
    /// Stops a manual payment counting, keeping the row as an audit record. The invoice is then
    /// recalculated through the one shared path, so a voided payment simply stops contributing —
    /// a fully paid invoice can go back to partially paid, or back to owing the whole amount.
    /// </summary>
    public async Task<PaymentDto> VoidPaymentAsync(
        Guid userId, Guid invoiceId, Guid paymentId, CancellationToken ct = default)
    {
        var invoice = await _db.Invoices.AsNoTracking()
                          .FirstOrDefaultAsync(i => i.Id == invoiceId && i.UserId == userId, ct)
                      ?? throw ApiException.NotFound("Invoice");

        // Scoped by invoice as well as tenant, so a payment id from another invoice — even one the
        // caller owns — cannot be voided through this invoice's route.
        var payment = await _db.Payments
                          .FirstOrDefaultAsync(p => p.Id == paymentId
                                                    && p.InvoiceId == invoice.Id
                                                    && p.UserId == userId, ct)
                      ?? throw ApiException.NotFound("Payment");

        if (payment.Source != PaymentSource.Manual)
            throw ApiException.Conflict(
                "This payment was collected online and cannot be voided here. Refund it through your payment provider instead.");

        if (payment.IsVoided)
            throw ApiException.Conflict("This payment has already been voided.");

        payment.VoidedAt = DateTime.UtcNow;
        payment.ConcurrencyStamp = Guid.NewGuid();
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Voided manual payment {PaymentId} of {Amount} {Currency} on invoice {InvoiceId}",
            payment.Id, payment.Amount, payment.Currency, invoice.Id);

        await RecalculateInvoiceAsync(invoice.Id, ct);

        return Map(payment);
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

        // A cancelled invoice is void and keeps that status whatever the ledger says.
        if (invoice.Status != InvoiceStatus.Cancelled)
        {
            InvoiceStatus? next = null;

            if (paid > 0)
            {
                next = paid >= invoice.GrandTotal ? InvoiceStatus.Paid : InvoiceStatus.PartiallyPaid;
            }
            else if (invoice.Status is InvoiceStatus.Paid or InvoiceStatus.PartiallyPaid)
            {
                // Nothing counts any more — every payment was voided. The invoice must not keep
                // claiming it was paid, so it returns to being issued and awaiting payment.
                // Only the two payment-derived statuses are rewound: an invoice the owner set by
                // hand to something else keeps their choice, as V2.2 intended.
                next = InvoiceStatus.Sent;
            }

            if (next is not null && invoice.Status != next)
            {
                _logger.LogInformation(
                    "Invoice {InvoiceId} moves {From} → {To} ({Paid} of {Total} {Currency})",
                    invoiceId, invoice.Status, next, paid, invoice.GrandTotal, invoice.Currency);
                invoice.Status = next.Value;
                await _db.SaveChangesAsync(ct);
            }
        }

        if (paid > invoice.GrandTotal)
        {
            // Recorded truthfully — the money is real and the provider took it — but flagged for
            // a human. V2.3 deliberately does not attempt an automatic refund.
            _logger.LogWarning(
                "Invoice {InvoiceId} is overpaid by {Excess}: {Paid} captured against a total of {Total} {Currency}",
                invoiceId, paid - invoice.GrandTotal, paid, invoice.GrandTotal, invoice.Currency);
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
                payments.Where(p => p.CountsTowardPaid).Sum(p => p.Amount),
                payments.Any(p => p.Status == PaymentStatus.Pending)),
            Payments = payments.Select(Map).ToList()
        };
    }

    // ---- helpers --------------------------------------------------------

    /// <summary>
    /// The paid amount, always summed from the rows that count — never read from a column. This
    /// is the in-service form of the rule <see cref="InvoiceLedger.WithBalance"/> expresses for
    /// bulk queries: captured, and not voided.
    /// </summary>
    private async Task<decimal> CapturedTotalAsync(Guid invoiceId, CancellationToken ct) =>
        await _db.Payments.AsNoTracking()
            .Where(p => p.InvoiceId == invoiceId
                        && p.Status == PaymentStatus.Captured
                        && p.VoidedAt == null)
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
        // Clamped so the customer is never shown a negative balance. The excess is not discarded:
        // it is reported separately, as an anomaly the owner has to resolve.
        var outstanding = Math.Max(0m, invoice.GrandTotal - paid);
        var overpaidBy = Math.Max(0m, paid - invoice.GrandTotal);

        return new PaymentSummaryDto
        {
            Total = invoice.GrandTotal,
            Paid = paid,
            Outstanding = outstanding,
            OverpaidBy = overpaidBy,
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
        Source = p.Source.ToString(),
        Provider = p.Provider,
        // A gateway payment is identified by the provider's reference; a manual one by whatever
        // the owner wrote down. One field, because the history shows them in one list.
        Reference = p.Source == PaymentSource.Manual ? p.Reference : p.ProviderPaymentId,
        OrderReference = p.ProviderOrderId,
        Method = p.Method,
        Notes = p.Notes,
        FailureReason = p.FailureReason,
        PaidAt = p.PaidAt,
        CreatedAt = p.CreatedAt,
        VoidedAt = p.VoidedAt,
        IsVoided = p.IsVoided,
        CanVoid = p.CanBeVoided
    };
}
