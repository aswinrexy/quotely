using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Quotely.Api.Data;
using Quotely.Api.Models;

namespace Quotely.Api.Billing;

/// <summary>
/// One thing a business might be allowed or not allowed to do. Named rather than stringly-typed,
/// so a typo in a controller attribute is a compile error rather than a silently ungated endpoint.
/// </summary>
public enum Entitlement
{
    UseProduct,
    ViewExistingData,
    ExportData,
    ManageBilling,
    CreateQuotation,
    CreateInvoice,
    CreateCustomer,
    CreateProduct,
    ConnectPayments,
    AcceptPayments
}

/// <summary>
/// THE one place that answers "may this business do that?".
///
/// Scattering <c>if (subscription…)</c> through controllers produces a set of rules nobody can
/// state, in which one endpoint is gated and its neighbour is not. Every gate in the application
/// resolves here, and the rules themselves come from configuration.
///
/// Three deliberate leniencies, all of which are about not punishing people for our own timing:
///
///   A subscription is never refused because a webhook is late — PastDue still grants access,
///   and a cancelled subscription keeps working to the end of the period it paid for.
///
///   A limit stops the NEXT one, never the ones already made. A business that drops to the free
///   tier with forty invoices keeps all forty; it simply cannot add a forty-first.
///
///   Enforcement is off unless configuration turns it on.
/// </summary>
public interface ISubscriptionEntitlementService
{
    Task<EntitlementDecision> CheckAsync(Guid userId, Entitlement entitlement, CancellationToken ct = default);

    Task<bool> IsAllowedAsync(Guid userId, Entitlement entitlement, CancellationToken ct = default);

    /// <summary>The full picture, for the billing page and for rendering what is locked.</summary>
    Task<EntitlementSummary> DescribeAsync(Guid userId, CancellationToken ct = default);
}

/// <summary>
/// Whether something is allowed, and — when it is not — why, in words meant for the owner.
///
/// "No" and "no, because you have used all ten" are different answers and lead to different
/// screens, so the reason travels with the decision rather than being reconstructed by the caller.
/// </summary>
public sealed record EntitlementDecision
{
    public required bool Allowed { get; init; }
    public string? Reason { get; init; }
    /// <summary>How many of the allowance are used, when the refusal was a limit.</summary>
    public int? Used { get; init; }
    public int? Limit { get; init; }

    public static EntitlementDecision Yes() => new() { Allowed = true };

    public static EntitlementDecision No(string reason) => new() { Allowed = false, Reason = reason };

    public static EntitlementDecision AtLimit(string what, int used, int limit) => new()
    {
        Allowed = false,
        Used = used,
        Limit = limit,
        Reason = $"Your free plan includes {limit} {what}. Upgrade to Quotely Pro to add more.",
    };
}

/// <summary>How much of a counted allowance a business has used.</summary>
public sealed record UsageQuota(int Used, int? Limit)
{
    public bool HasLimit => Limit is not null;
    public bool Exhausted => Limit is not null && Used >= Limit;
    public int? Remaining => Limit is null ? null : Math.Max(0, Limit.Value - Used);
}

/// <summary>What a business may currently do. Rendered as the locks on the interface.</summary>
public sealed record EntitlementSummary
{
    public required bool HasAccess { get; init; }
    public required SubscriptionStatus Status { get; init; }
    public DateTime? AccessEndsAt { get; init; }
    public required bool EnforcementEnabled { get; init; }

    public required bool CanCreateQuotation { get; init; }
    public required bool CanConnectPayments { get; init; }
    public required bool CanAcceptPayments { get; init; }
    public required bool CanExportData { get; init; }

    public required UsageQuota Invoices { get; init; }
    public required UsageQuota Customers { get; init; }
    public required UsageQuota Products { get; init; }
}

public class SubscriptionEntitlementService : ISubscriptionEntitlementService
{
    private readonly AppDbContext _db;
    private readonly ISubscriptionService _subscriptions;
    private readonly BillingOptions _options;

    public SubscriptionEntitlementService(
        AppDbContext db, ISubscriptionService subscriptions, IOptions<BillingOptions> options)
    {
        _db = db;
        _subscriptions = subscriptions;
        _options = options.Value;
    }

    public async Task<bool> IsAllowedAsync(
        Guid userId, Entitlement entitlement, CancellationToken ct = default) =>
        (await CheckAsync(userId, entitlement, ct)).Allowed;

    public async Task<EntitlementDecision> CheckAsync(
        Guid userId, Entitlement entitlement, CancellationToken ct = default)
    {
        // Enforcement off: everything is permitted, and no subscription is consulted at all.
        // This must be genuinely inert rather than "allowed but quietly recorded as a violation".
        if (!_options.EnforceEntitlements) return EntitlementDecision.Yes();

        var subscription = await _subscriptions.GetOrCreateAsync(userId, ct);

        // A paid or trialing subscription has no limits of any kind.
        if (subscription.GrantsAccess(DateTime.UtcNow)) return EntitlementDecision.Yes();

        var free = _options.WithoutSubscription;

        return entitlement switch
        {
            Entitlement.UseProduct => Allow(free.CanSignIn),
            Entitlement.ViewExistingData => Allow(free.CanViewExistingData),
            Entitlement.ExportData => Allow(free.CanExportData),
            Entitlement.ManageBilling => Allow(free.CanManageBilling),
            Entitlement.AcceptPayments => Allow(free.CanAcceptPayments),

            Entitlement.CreateQuotation => free.CanCreateQuotation
                ? EntitlementDecision.Yes()
                : EntitlementDecision.No(
                    "Quotations are part of Quotely Pro. Upgrade to start sending them."),

            Entitlement.ConnectPayments => free.CanConnectPayments
                ? EntitlementDecision.Yes()
                : EntitlementDecision.No(
                    "Online payments are part of Quotely Pro. You can still record cash, cheque and bank transfers."),

            Entitlement.CreateInvoice => await CheckQuotaAsync(
                "invoices", free.MaxInvoices, _db.Invoices.Where(i => i.UserId == userId), ct),

            Entitlement.CreateCustomer => await CheckQuotaAsync(
                "customers", free.MaxCustomers, _db.Customers.Where(c => c.UserId == userId), ct),

            Entitlement.CreateProduct => await CheckQuotaAsync(
                "products and services", free.MaxProducts, _db.Products.Where(p => p.UserId == userId), ct),

            _ => EntitlementDecision.No("That is part of Quotely Pro."),
        };
    }

    private static EntitlementDecision Allow(bool allowed) =>
        allowed ? EntitlementDecision.Yes() : EntitlementDecision.No("That is part of Quotely Pro.");

    /// <summary>
    /// Counts what exists and compares it to the allowance.
    ///
    /// The count is taken at the moment of the request rather than cached. Two simultaneous
    /// creations could therefore both pass on the tenth — a race worth exactly nothing to exploit,
    /// and not worth a lock on every insert to close.
    /// </summary>
    private static async Task<EntitlementDecision> CheckQuotaAsync<T>(
        string what, int? limit, IQueryable<T> owned, CancellationToken ct)
    {
        if (limit is null) return EntitlementDecision.Yes();

        var used = await owned.CountAsync(ct);
        return used >= limit ? EntitlementDecision.AtLimit(what, used, limit.Value) : EntitlementDecision.Yes();
    }

    public async Task<EntitlementSummary> DescribeAsync(Guid userId, CancellationToken ct = default)
    {
        var subscription = await _subscriptions.GetOrCreateAsync(userId, ct);
        var now = DateTime.UtcNow;
        var enforced = _options.EnforceEntitlements;
        var hasAccess = !enforced || subscription.GrantsAccess(now);
        var free = _options.WithoutSubscription;

        // Counted once, for the whole summary. A paid account is not counted at all — there is
        // nothing to compare against, and the query would be wasted work on every page load.
        var quota = hasAccess
            ? new { Invoices = 0, Customers = 0, Products = 0 }
            : new
            {
                Invoices = await _db.Invoices.CountAsync(i => i.UserId == userId, ct),
                Customers = await _db.Customers.CountAsync(c => c.UserId == userId, ct),
                Products = await _db.Products.CountAsync(p => p.UserId == userId, ct),
            };

        return new EntitlementSummary
        {
            HasAccess = hasAccess,
            Status = subscription.Status,
            AccessEndsAt = subscription.AccessEndsAt(now),
            EnforcementEnabled = enforced,

            CanCreateQuotation = hasAccess || free.CanCreateQuotation,
            CanConnectPayments = hasAccess || free.CanConnectPayments,
            CanAcceptPayments = hasAccess || free.CanAcceptPayments,
            CanExportData = hasAccess || free.CanExportData,

            Invoices = new UsageQuota(quota.Invoices, hasAccess ? null : free.MaxInvoices),
            Customers = new UsageQuota(quota.Customers, hasAccess ? null : free.MaxCustomers),
            Products = new UsageQuota(quota.Products, hasAccess ? null : free.MaxProducts),
        };
    }
}
