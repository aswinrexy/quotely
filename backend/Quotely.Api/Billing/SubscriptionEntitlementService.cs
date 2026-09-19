using Microsoft.Extensions.Options;
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
    AcceptPayments
}

/// <summary>
/// THE one place that answers "may this business do that?".
///
/// The brief for this milestone asked for exactly this and said why: scattering
/// <c>if (subscription…)</c> through controllers produces a set of rules nobody can state, in
/// which one endpoint is gated and its neighbour is not. Every gate in the application resolves
/// here, and the rules themselves come from configuration so they can be changed without a
/// deployment.
///
/// Two deliberate leniencies, both of which are about not punishing people for our own timing:
///
///   A subscription is never refused because a webhook is late. PastDue still grants access, and
///   a cancelled subscription keeps working to the end of the period it paid for.
///
///   Enforcement is off unless configuration turns it on. A deployment that has not decided to
///   charge anyone does not lock anyone out.
/// </summary>
public interface ISubscriptionEntitlementService
{
    Task<bool> IsAllowedAsync(Guid userId, Entitlement entitlement, CancellationToken ct = default);

    /// <summary>The full picture, for the billing page and the admin view.</summary>
    Task<EntitlementSummary> DescribeAsync(Guid userId, CancellationToken ct = default);
}

/// <summary>What a business may currently do, and until when.</summary>
public sealed record EntitlementSummary
{
    public required bool HasAccess { get; init; }
    public required SubscriptionStatus Status { get; init; }
    public DateTime? AccessEndsAt { get; init; }
    public required bool CanCreateQuotation { get; init; }
    public required bool CanCreateInvoice { get; init; }
    public required bool CanAcceptPayments { get; init; }
    public required bool CanExportData { get; init; }
    public required bool EnforcementEnabled { get; init; }
}

public class SubscriptionEntitlementService : ISubscriptionEntitlementService
{
    private readonly ISubscriptionService _subscriptions;
    private readonly BillingOptions _options;

    public SubscriptionEntitlementService(
        ISubscriptionService subscriptions, IOptions<BillingOptions> options)
    {
        _subscriptions = subscriptions;
        _options = options.Value;
    }

    public async Task<bool> IsAllowedAsync(
        Guid userId, Entitlement entitlement, CancellationToken ct = default)
    {
        // Enforcement off: everything is permitted, and no subscription is consulted at all.
        // This is the state the product ships in, and it must be genuinely inert rather than
        // "allowed but quietly recorded as a violation".
        if (!_options.EnforceEntitlements) return true;

        var subscription = await _subscriptions.GetOrCreateAsync(userId, ct);

        if (subscription.GrantsAccess(DateTime.UtcNow)) return true;

        // Beyond this line the business has no current subscription. What remains is what
        // configuration says someone in that position may still do.
        var restricted = _options.WithoutSubscription;

        return entitlement switch
        {
            Entitlement.UseProduct => restricted.CanSignIn,
            Entitlement.ViewExistingData => restricted.CanViewExistingData,
            Entitlement.ExportData => restricted.CanExportData,
            Entitlement.ManageBilling => restricted.CanManageBilling,
            Entitlement.CreateQuotation => restricted.CanCreateQuotation,
            Entitlement.CreateInvoice => restricted.CanCreateInvoice,
            Entitlement.AcceptPayments => restricted.CanAcceptPayments,
            _ => false
        };
    }

    public async Task<EntitlementSummary> DescribeAsync(Guid userId, CancellationToken ct = default)
    {
        var subscription = await _subscriptions.GetOrCreateAsync(userId, ct);
        var now = DateTime.UtcNow;
        var hasAccess = !_options.EnforceEntitlements || subscription.GrantsAccess(now);
        var restricted = _options.WithoutSubscription;

        return new EntitlementSummary
        {
            HasAccess = hasAccess,
            Status = subscription.Status,
            AccessEndsAt = subscription.AccessEndsAt(now),
            CanCreateQuotation = hasAccess || restricted.CanCreateQuotation,
            CanCreateInvoice = hasAccess || restricted.CanCreateInvoice,
            CanAcceptPayments = hasAccess || restricted.CanAcceptPayments,
            CanExportData = hasAccess || restricted.CanExportData,
            EnforcementEnabled = _options.EnforceEntitlements
        };
    }
}
