using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Quotely.Api.Data;
using Quotely.Api.DTOs;
using Quotely.Api.Middleware;
using Quotely.Api.Models;
using Quotely.Api.Payments;

namespace Quotely.Api.Services;

public interface IPublicInvoiceService
{
    Task<PublicInvoiceLinkDto> CreateLinkAsync(Guid userId, Guid invoiceId, CancellationToken ct = default);
    Task<PublicInvoiceDto> GetAsync(string token, CancellationToken ct = default);
    Task<PaymentOrderDto> CreatePaymentOrderAsync(string token, CancellationToken ct = default);
    Task<VerifyPaymentResponse> VerifyPaymentAsync(string token, VerifyPaymentRequest request, CancellationToken ct = default);
    Task<(Invoice Invoice, BusinessProfile? Business)> GetForPdfAsync(string token, CancellationToken ct = default);
}

/// <summary>
/// The customer-facing invoice, reached by a bearer token rather than a session. Deliberately
/// built on the same primitives as the quotation share link — <see cref="PublicTokenGenerator"/>
/// and hash-only storage — so there is one public-link security model in this codebase, not two.
/// </summary>
public class PublicInvoiceService : IPublicInvoiceService
{
    private readonly AppDbContext _db;
    private readonly IPaymentService _payments;
    private readonly IMerchantPaymentProvider _provider;
    private readonly IMerchantConnectionService _merchants;
    private readonly PublicLinkOptions _options;
    private readonly ILogger<PublicInvoiceService> _logger;

    public PublicInvoiceService(
        AppDbContext db,
        IPaymentService payments,
        IMerchantPaymentProvider provider,
        IMerchantConnectionService merchants,
        IOptions<PublicLinkOptions> options,
        ILogger<PublicInvoiceService> logger)
    {
        _db = db;
        _payments = payments;
        _provider = provider;
        _merchants = merchants;
        _options = options.Value;
        _logger = logger;
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    // ---- owner ----------------------------------------------------------

    /// <summary>
    /// Mints the payment link for one of the caller's own invoices. Only the hash is kept, so the
    /// URL in this response is the one and only copy; asking again issues a new link and retires
    /// the old one, which is also how a link is revoked.
    /// </summary>
    public async Task<PublicInvoiceLinkDto> CreateLinkAsync(Guid userId, Guid invoiceId, CancellationToken ct = default)
    {
        var invoice = await _db.Invoices.FirstOrDefaultAsync(i => i.Id == invoiceId && i.UserId == userId, ct)
                      ?? throw ApiException.NotFound("Invoice");

        if (invoice.Status == InvoiceStatus.Cancelled)
            throw ApiException.Conflict("A cancelled invoice cannot be shared.");

        var token = PublicTokenGenerator.CreateToken();
        invoice.PublicTokenHash = PublicTokenGenerator.Hash(token);
        invoice.PublicLinkCreatedAt = DateTime.UtcNow;

        // Sharing the link is the act of issuing the invoice, mirroring what quotations do.
        // Anything already beyond Draft keeps the status its owner gave it.
        if (invoice.Status == InvoiceStatus.Draft)
            invoice.Status = InvoiceStatus.Sent;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Created public payment link for invoice {InvoiceId}", invoice.Id);

        var url = BuildUrl(token);

        // The share material is composed now, in the same response, because this is the only
        // moment the URL exists: the database keeps a hash, so it can never be recovered later.
        var business = await LoadBusinessAsync(invoice.UserId, ct);
        var summary = await _payments.GetSummaryAsync(invoice.Id, ct);
        var share = InvoiceShareBuilder.Build(
            invoice,
            business?.BusinessName ?? "Your business",
            // What the customer actually owes. Part-paid invoices must not ask for the full total.
            summary.Outstanding > 0 ? summary.Outstanding : invoice.GrandTotal,
            url);

        return new PublicInvoiceLinkDto(url, invoice.PublicLinkCreatedAt.Value, share);
    }

    // ---- customer -------------------------------------------------------

    public async Task<PublicInvoiceDto> GetAsync(string token, CancellationToken ct = default)
    {
        var invoice = await FindByTokenAsync(token, tracking: false, ct);
        return await MapAsync(invoice, ct);
    }

    public async Task<PaymentOrderDto> CreatePaymentOrderAsync(string token, CancellationToken ct = default)
    {
        // Note what this method does not accept: an amount, a currency, an invoice id. The token
        // selects the invoice and the server computes everything else.
        var invoice = await FindByTokenAsync(token, tracking: true, ct);
        var business = await LoadBusinessAsync(invoice.UserId, ct);

        // The merchant is resolved from the INVOICE'S OWN TENANT. Not from the request, not from
        // configuration, not from a default. This single line is what sends the customer's money
        // to the business that issued the invoice.
        var merchant = await _merchants.ResolveAsync(invoice.UserId, ct);

        var (payment, order) = await _payments.StartPaymentAsync(invoice, merchant, ct);

        return new PaymentOrderDto
        {
            // The merchant's own publishable key — under OAuth, their public_token. Never a
            // key belonging to Quotely or to any other business.
            KeyId = merchant.Credentials.PublicKey,
            OrderId = order.OrderId,
            Amount = order.AmountInMinorUnits,
            Currency = order.Currency,
            InvoiceNumber = invoice.InvoiceNumber,
            BusinessName = business?.BusinessName ?? "Invoice",
            CustomerName = payment.CustomerName,
            CustomerEmail = payment.CustomerEmail,
            CustomerContact = invoice.CustomerPhone
        };
    }

    /// <summary>
    /// Confirms a checkout result. The signature proves the provider produced it, and the
    /// provider itself is then asked what really happened — the browser's claim of success is
    /// never sufficient on its own.
    /// </summary>
    public async Task<VerifyPaymentResponse> VerifyPaymentAsync(
        string token, VerifyPaymentRequest request, CancellationToken ct = default)
    {
        var invoice = await FindByTokenAsync(token, tracking: false, ct);
        var merchant = await _merchants.ResolveAsync(invoice.UserId, ct);

        // The order must be one we created for this very invoice. Without this check a valid
        // signature from someone else's order would credit the wrong invoice.
        var order = await _db.Payments.AsNoTracking()
            .FirstOrDefaultAsync(p => p.ProviderOrderId == request.RazorpayOrderId, ct);

        if (order is null || order.InvoiceId != invoice.Id)
        {
            _logger.LogWarning(
                "Rejected verification: order {ProviderOrderId} does not belong to invoice {InvoiceId}",
                request.RazorpayOrderId, invoice.Id);
            throw ApiException.BadRequest("This payment does not belong to this invoice.");
        }

        try
        {
            var verification = _provider.VerifyCheckoutSignature(
                merchant,
                new CheckoutResult(request.RazorpayOrderId, request.RazorpayPaymentId, request.RazorpaySignature));

            if (verification == CheckoutVerification.NotVerifiable)
            {
                // An OAuth connection: we hold no signing secret for this merchant, by design.
                // The provider lookup below is then not a corroboration but the whole proof, and
                // it is a sound one — it asks Razorpay directly, with the merchant's own token,
                // what happened to this payment id.
                _logger.LogInformation(
                    "Checkout signature for invoice {InvoiceId} cannot be verified locally; " +
                    "confirming with the provider instead", invoice.Id);
            }
        }
        catch (PaymentSignatureException ex)
        {
            _logger.LogWarning(
                "Signature verification failed for payment {ProviderPaymentId} on invoice {InvoiceId}: {Reason}",
                request.RazorpayPaymentId, invoice.Id, ex.Message);
            throw ApiException.BadRequest("This payment could not be verified.");
        }

        // Ask the provider for the truth rather than trusting the callback's implied outcome.
        PaymentOutcome outcome;
        try
        {
            outcome = await _provider.GetPaymentAsync(merchant, request.RazorpayPaymentId, ct);
        }
        catch (PaymentCredentialException ex)
        {
            _logger.LogWarning(ex,
                "Razorpay refused the credentials of merchant {UserId} while confirming a payment", invoice.UserId);
            throw new ApiException(System.Net.HttpStatusCode.BadGateway,
                "We could not confirm this payment yet. It will be updated automatically once the provider confirms it.");
        }
        catch (PaymentProviderException ex)
        {
            _logger.LogWarning(ex,
                "Could not confirm payment {ProviderPaymentId} with the provider", request.RazorpayPaymentId);
            throw new ApiException(System.Net.HttpStatusCode.BadGateway,
                "We could not confirm this payment yet. It will be updated automatically once the provider confirms it.");
        }

        // The provider must agree that this payment belongs to the order we opened. Checking it
        // here means the browser's claimed pairing is corroborated rather than believed.
        if (!string.Equals(outcome.ProviderOrderId, request.RazorpayOrderId, StringComparison.Ordinal) ||
            !string.Equals(outcome.ProviderPaymentId, request.RazorpayPaymentId, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Rejected verification: the provider reports payment {ActualPayment} on order {ActualOrder}, " +
                "not the pairing that was submitted for invoice {InvoiceId}",
                outcome.ProviderPaymentId, outcome.ProviderOrderId, invoice.Id);
            throw ApiException.BadRequest("This payment does not belong to this invoice.");
        }

        // One shared path into the database, used by the webhook too. The outcome is passed
        // through exactly as the provider stated it — no field is substituted with a value the
        // browser supplied.
        var payment = await _payments.ProcessOutcomeAsync(outcome, merchant, ct);

        var summary = await _payments.GetSummaryAsync(invoice.Id, ct);

        return new VerifyPaymentResponse
        {
            Success = payment.Status == PaymentStatus.Captured,
            PaymentStatus = payment.Status.ToString(),
            InvoiceStatus = summary.InvoiceStatus,
            Total = summary.Total,
            Paid = summary.Paid,
            Outstanding = summary.Outstanding,
            AmountPaid = payment.Status == PaymentStatus.Captured ? payment.Amount : 0m,
            Currency = summary.Currency,
            PaymentReference = payment.ProviderPaymentId,
            Message = payment.Status switch
            {
                PaymentStatus.Captured => null,
                PaymentStatus.Pending => "Your payment is being confirmed. There is no need to pay again.",
                PaymentStatus.Failed => payment.FailureReason ?? "The payment could not be completed.",
                _ => "The payment is still being processed."
            }
        };
    }

    public async Task<(Invoice Invoice, BusinessProfile? Business)> GetForPdfAsync(string token, CancellationToken ct = default)
    {
        var invoice = await FindByTokenAsync(token, tracking: false, ct);
        return (invoice, await LoadBusinessAsync(invoice.UserId, ct));
    }

    // ---- helpers --------------------------------------------------------

    /// <summary>
    /// The hash is the only lookup key. An unknown, malformed or revoked token is an identical
    /// 404, so a caller cannot learn whether any given invoice exists.
    /// </summary>
    private async Task<Invoice> FindByTokenAsync(string token, bool tracking, CancellationToken ct)
    {
        if (!PublicTokenGenerator.LooksValid(token))
            throw ApiException.NotFound("Invoice");

        var hash = PublicTokenGenerator.Hash(token);

        var query = _db.Invoices
            .Include(i => i.Items.OrderBy(x => x.SortOrder))
            .Where(i => i.PublicTokenHash == hash);

        if (!tracking) query = query.AsNoTracking();

        var invoice = await query.FirstOrDefaultAsync(ct) ?? throw ApiException.NotFound("Invoice");

        // A draft is not an issued document; a link to one behaves as if it does not exist.
        if (invoice.Status == InvoiceStatus.Draft)
            throw ApiException.NotFound("Invoice");

        return invoice;
    }

    private Task<BusinessProfile?> LoadBusinessAsync(Guid userId, CancellationToken ct) =>
        _db.BusinessProfiles.AsNoTracking().FirstOrDefaultAsync(b => b.UserId == userId, ct);

    private string BuildUrl(string token) => $"{_options.BaseUrl.TrimEnd('/')}/i/{token}";

    private async Task<PublicInvoiceDto> MapAsync(Invoice invoice, CancellationToken ct)
    {
        var business = await LoadBusinessAsync(invoice.UserId, ct);
        var summary = await _payments.GetSummaryAsync(invoice.Id, ct);

        // Whether THIS business can be paid online, not whether Quotely can. TryResolve rather
        // than Resolve: a business with no payment connection is an ordinary state to render, not
        // an error to throw at a customer who only wanted to read their invoice.
        var canPayOnline = await _merchants.TryResolveAsync(invoice.UserId, ct) is not null;

        // THE RULE, as InvoiceLedger states it: captured and not voided. The summary above already
        // excludes voided rows, so listing them here would show a customer payments that visibly
        // do not add up to the balance they are being asked to settle.
        var settled = await _db.Payments.AsNoTracking()
            .Where(p => p.InvoiceId == invoice.Id
                        && p.Status == PaymentStatus.Captured
                        && p.VoidedAt == null)
            .OrderByDescending(p => p.PaidAt)
            .Select(p => new PublicPaymentDto
            {
                Amount = p.Amount,
                Method = p.Method,
                Reference = p.ProviderPaymentId,
                PaidAt = p.PaidAt
            })
            .ToListAsync(ct);

        return new PublicInvoiceDto
        {
            InvoiceNumber = invoice.InvoiceNumber,
            InvoiceDate = invoice.InvoiceDate,
            DueDate = invoice.DueDate,
            Business = new PublicBusinessDto
            {
                BusinessName = business?.BusinessName ?? "Invoice",
                Email = business?.BusinessEmail,
                Phone = business?.Phone,
                AddressLine = business?.AddressLine,
                City = business?.City,
                State = business?.State,
                PostalCode = business?.PostalCode,
                Country = business?.Country,
                TaxNumber = business?.TaxNumber,
                LogoUrl = business?.LogoUrl
            },
            // The invoice's own billing snapshot, not the live customer record.
            Customer = new PublicCustomerDto
            {
                Name = invoice.CustomerName,
                CompanyName = invoice.CustomerCompanyName,
                Email = invoice.CustomerEmail,
                Phone = invoice.CustomerPhone,
                AddressLine = invoice.CustomerAddressLine,
                City = invoice.CustomerCity,
                State = invoice.CustomerState,
                PostalCode = invoice.CustomerPostalCode,
                Country = invoice.CustomerCountry
            },
            Items = invoice.Items.OrderBy(i => i.SortOrder).Select(i => new PublicInvoiceItemDto
            {
                Name = i.Name,
                Description = i.Description,
                Unit = i.Unit,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice,
                Discount = i.Discount,
                TaxRate = i.TaxRate,
                LineTotal = i.LineTotal
            }).ToList(),
            Subtotal = invoice.Subtotal,
            DiscountTotal = invoice.DiscountTotal,
            TaxTotal = invoice.TaxTotal,
            GrandTotal = invoice.GrandTotal,
            Currency = invoice.Currency,
            Notes = invoice.Notes,
            Terms = invoice.Terms,
            Status = invoice.Status.ToString(),
            IsOverdue = invoice.IsOverdue(Today),
            Paid = summary.Paid,
            Outstanding = summary.Outstanding,
            // Paying online also requires THIS business to have connected its own payment
            // account. Without that the page shows the balance and the bank details the business
            // put on the invoice, rather than a Pay button that would fail when pressed.
            CanPay = summary.CanPay && canPayOnline,
            HasPendingPayment = summary.HasPendingPayment,
            Payments = settled
        };
    }
}
