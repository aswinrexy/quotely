using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Quotely.Api.Billing;
using Quotely.Api.Data;
using Quotely.Api.Models;

namespace Quotely.Api.Controllers;

/// <summary>
/// The internal overview: which businesses are subscribed, which can take payments, and when
/// each was last heard from.
///
/// Deliberately small. It is one read-only endpoint answering the questions that would otherwise
/// be answered by opening the database, and it is not the beginning of an admin system.
///
/// Nothing it returns is a secret. No key, no token, no webhook secret, no connection id, and no
/// customer data at all — only each business's own name, its subscription state and its payment
/// connection state.
/// </summary>
[ApiController]
[Authorize]
[Route("api/admin/billing")]
public class AdminBillingController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AdminOptions _options;
    private readonly ILogger<AdminBillingController> _logger;

    public AdminBillingController(
        AppDbContext db, IOptions<AdminOptions> options, ILogger<AdminBillingController> logger)
    {
        _db = db;
        _options = options.Value;
        _logger = logger;
    }

    [HttpGet("accounts")]
    public async Task<ActionResult<IReadOnlyList<AdminAccountDto>>> Accounts(
        [FromQuery] int take = 100, CancellationToken ct = default)
    {
        // 404, not 403. A non-admin learns that this route does nothing for them, not that it
        // exists and is guarded — which is how the rest of the application answers a request for
        // something that is not the caller's.
        // Both spellings. Whether JwtBearer maps the "email" claim onto the long-form
        // ClaimTypes.Email URI depends on the handler's inbound claim mapping, and an admin check
        // that silently reads nothing would lock the founder out of their own overview.
        var email = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email");

        if (!_options.IsAdmin(email))
        {
            _logger.LogWarning("Refused an admin billing request from a non-admin account");
            return NotFound();
        }

        take = Math.Clamp(take, 1, 500);

        var accounts = await _db.Users.AsNoTracking()
            .OrderByDescending(u => u.CreatedAt)
            .Take(take)
            .Select(u => new AdminAccountDto
            {
                BusinessName = u.BusinessProfile!.BusinessName,
                CreatedAt = u.CreatedAt,

                // ---- what they pay us ----
                SubscriptionStatus = _db.Subscriptions
                    .Where(s => s.UserId == u.Id)
                    .Select(s => (SubscriptionStatus?)s.Status)
                    .FirstOrDefault(),
                PlanCode = _db.Subscriptions
                    .Where(s => s.UserId == u.Id).Select(s => s.PlanCode).FirstOrDefault(),
                TrialEnd = _db.Subscriptions
                    .Where(s => s.UserId == u.Id).Select(s => s.TrialEnd).FirstOrDefault(),
                LastSubscriptionPaymentAt = _db.Subscriptions
                    .Where(s => s.UserId == u.Id).Select(s => s.LastPaymentAt).FirstOrDefault(),
                CouponCode = _db.CouponRedemptions
                    .Where(r => r.UserId == u.Id)
                    .OrderByDescending(r => r.RedeemedAt)
                    .Select(r => r.CodeUsed)
                    .FirstOrDefault(),

                // ---- what their customers pay them ----
                PaymentConnectionStatus = _db.MerchantPaymentConnections
                    .Where(c => c.UserId == u.Id)
                    .Select(c => (MerchantConnectionStatus?)c.Status)
                    .FirstOrDefault(),
                PaymentConnectionMode = _db.MerchantPaymentConnections
                    .Where(c => c.UserId == u.Id)
                    .Select(c => (MerchantConnectionMode?)c.Mode)
                    .FirstOrDefault(),
                // The provider's own account identifier, which is safe to show: it names an
                // account without granting any access to it.
                ProviderAccountId = _db.MerchantPaymentConnections
                    .Where(c => c.UserId == u.Id).Select(c => c.ProviderAccountId).FirstOrDefault(),
                PaymentConnectionVerifiedAt = _db.MerchantPaymentConnections
                    .Where(c => c.UserId == u.Id).Select(c => c.LastVerifiedAt).FirstOrDefault(),

                LastWebhookAt = _db.WebhookEvents
                    .Where(e => e.UserId == u.Id)
                    .OrderByDescending(e => e.ReceivedAt)
                    .Select(e => (DateTime?)e.ReceivedAt)
                    .FirstOrDefault(),
                LastInvoicePaymentAt = _db.Payments
                    .Where(p => p.UserId == u.Id && p.Status == PaymentStatus.Captured && p.VoidedAt == null)
                    .OrderByDescending(p => p.PaidAt)
                    .Select(p => p.PaidAt)
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        return Ok(accounts);
    }
}

/// <summary>
/// One business, as the internal overview describes it. Every field here is either the
/// business's own name or a status — never a credential, and never anything about their customers.
/// </summary>
public class AdminAccountDto
{
    public string BusinessName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public SubscriptionStatus? SubscriptionStatus { get; set; }
    public string? PlanCode { get; set; }
    public DateTime? TrialEnd { get; set; }
    public DateTime? LastSubscriptionPaymentAt { get; set; }
    public string? CouponCode { get; set; }

    public MerchantConnectionStatus? PaymentConnectionStatus { get; set; }
    public MerchantConnectionMode? PaymentConnectionMode { get; set; }
    public string? ProviderAccountId { get; set; }
    public DateTime? PaymentConnectionVerifiedAt { get; set; }

    public DateTime? LastWebhookAt { get; set; }
    public DateTime? LastInvoicePaymentAt { get; set; }
}
