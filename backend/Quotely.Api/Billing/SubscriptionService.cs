using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Quotely.Api.Data;
using Quotely.Api.DTOs;
using Quotely.Api.Middleware;
using Quotely.Api.Models;
using Quotely.Api.Payments;

namespace Quotely.Api.Billing;

public interface ISubscriptionService
{
    /// <summary>
    /// Returns the business's subscription, creating a trial for it the first time it is asked
    /// for. New accounts therefore start with full access and an end date, without registration
    /// needing to know that billing exists.
    /// </summary>
    Task<Subscription> GetOrCreateAsync(Guid userId, CancellationToken ct = default);

    /// <summary>What the billing page shows. Contains no provider secret.</summary>
    Task<SubscriptionDto> GetStatusAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Redeems a coupon against this business's subscription, extending the free period.
    /// Atomic, once per account, and validated entirely on the server.
    /// </summary>
    Task<SubscriptionDto> RedeemCouponAsync(Guid userId, string code, CancellationToken ct = default);

    /// <summary>
    /// Creates the subscription at Razorpay and returns what the browser needs to open its
    /// checkout. Billing starts when the free period ends, not immediately.
    /// </summary>
    Task<SubscriptionCheckoutDto> StartAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Confirms the mandate the business authorised, on the provider's word.</summary>
    Task<SubscriptionDto> ConfirmAsync(
        Guid userId, ConfirmSubscriptionRequest request, CancellationToken ct = default);

    /// <summary>Cancels at the end of the paid period, leaving access they have paid for.</summary>
    Task<SubscriptionDto> CancelAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Applies a provider subscription state to our record. The single path by which a webhook or
    /// a reconciliation changes a subscription — there are not two state machines here either.
    /// </summary>
    Task ApplyProviderStateAsync(
        ProviderSubscription state, bool charged, bool paymentFailed, CancellationToken ct = default);
}

/// <summary>
/// Quotely's own subscriptions: trials, coupons, mandates and cancellation.
///
/// Nothing in this file touches a merchant's payment connection, and nothing in the merchant
/// payment code touches a subscription. They are two different businesses' money.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    private readonly AppDbContext _db;
    private readonly ISaasBillingProvider _provider;
    private readonly BillingOptions _options;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(
        AppDbContext db,
        ISaasBillingProvider provider,
        IOptions<BillingOptions> options,
        ILogger<SubscriptionService> logger)
    {
        _db = db;
        _provider = provider;
        _options = options.Value;
        _logger = logger;
    }

    // ---- reading ---------------------------------------------------------

    public async Task<Subscription> GetOrCreateAsync(Guid userId, CancellationToken ct = default)
    {
        var subscription = await _db.Subscriptions
            .FirstOrDefaultAsync(s => s.UserId == userId, ct);

        if (subscription is not null) return subscription;

        var plan = _options.DefaultPlan;
        var now = DateTime.UtcNow;

        subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PlanCode = plan.Code,
            // Snapshotted, not referenced. A later price change must not rewrite what this
            // business was told they would pay.
            Price = plan.Price,
            Currency = plan.Currency,
            Provider = _provider.Name,
            Status = SubscriptionStatus.Trialing,
            TrialStart = now,
            TrialEnd = now.AddDays(_options.DefaultTrialDays)
        };

        _db.Subscriptions.Add(subscription);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two requests from the same new account raced. The unique index on UserId decided
            // it; the loser reads the winner's row rather than creating a second trial.
            _db.ChangeTracker.Clear();
            return await _db.Subscriptions.FirstAsync(s => s.UserId == userId, ct);
        }

        _logger.LogInformation(
            "Started a {Days}-day trial for business {UserId}", _options.DefaultTrialDays, userId);

        return subscription;
    }

    public async Task<SubscriptionDto> GetStatusAsync(Guid userId, CancellationToken ct = default)
    {
        var subscription = await GetOrCreateAsync(userId, ct);

        var redemption = await _db.CouponRedemptions.AsNoTracking()
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.RedeemedAt)
            .FirstOrDefaultAsync(ct);

        return Describe(subscription, redemption);
    }

    private SubscriptionDto Describe(Subscription subscription, CouponRedemption? redemption)
    {
        var now = DateTime.UtcNow;
        var plan = _options.FindPlan(subscription.PlanCode);

        return new SubscriptionDto
        {
            PlanCode = subscription.PlanCode,
            PlanName = plan?.Name ?? subscription.PlanCode,
            PlanDescription = plan?.Description,
            Price = subscription.Price,
            Currency = subscription.Currency,
            Interval = plan?.Interval ?? "monthly",
            Status = subscription.Status,
            StatusMessage = subscription.StatusMessage,
            TrialStart = subscription.TrialStart,
            TrialEnd = subscription.TrialEnd,
            InTrial = subscription.IsInTrial(now),
            CurrentPeriodStart = subscription.CurrentPeriodStart,
            CurrentPeriodEnd = subscription.CurrentPeriodEnd,
            CancelRequestedAt = subscription.CancelRequestedAt,
            CancelledAt = subscription.CancelledAt,
            LastPaymentAt = subscription.LastPaymentAt,
            HasActiveMandate = subscription.ProviderSubscriptionId is not null,
            HasAccess = !_options.EnforceEntitlements || subscription.GrantsAccess(now),
            AccessEndsAt = subscription.AccessEndsAt(now),
            // What the business will actually be charged next, and when.
            NextPaymentAt = NextPaymentAt(subscription, now),
            BillingEnabled = _options.Enabled,
            CouponCode = redemption?.CodeUsed,
            CouponRedeemedAt = redemption?.RedeemedAt,
            CouponFreeMonths = redemption?.FreeMonthsGranted
        };
    }

    /// <summary>
    /// When money is next taken. During a trial that is the trial's end; once active it is the
    /// end of the current period; and for anything cancelled or expired there is no next payment
    /// at all, which is worth saying explicitly rather than showing a stale date.
    /// </summary>
    private static DateTime? NextPaymentAt(Subscription subscription, DateTime now) => subscription.Status switch
    {
        SubscriptionStatus.Trialing => subscription.TrialEnd,
        SubscriptionStatus.Active => subscription.CurrentPeriodEnd,
        SubscriptionStatus.PastDue => now,
        _ => null
    };

    // ---- coupons ---------------------------------------------------------

    public async Task<SubscriptionDto> RedeemCouponAsync(
        Guid userId, string code, CancellationToken ct = default)
    {
        var normalised = (code ?? string.Empty).Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(normalised))
            throw ApiException.BadRequest("Enter a coupon code.");

        // Two independent races have to be survived here, and they need different mechanisms.
        //
        //   The same account redeeming twice is settled by the unique index on
        //   (CouponId, UserId). One insert wins; the other is told they have already used it.
        //
        //   Two DIFFERENT accounts exhausting a limited coupon's last slot is settled by the
        //   coupon's concurrency token, rotated on every increment. The second write finds the
        //   row changed and is retried against the new count — which is why the loop exists, and
        //   why neither of them is a transaction: an explicit transaction would serialise the
        //   read, but a read committed inside one still cannot see a sibling's uncommitted
        //   increment, so it would not actually prevent the over-redemption it looks like it does.
        for (var attempt = 0; attempt < RedemptionAttempts; attempt++)
        {
            try
            {
                return await TryRedeemAsync(userId, normalised, ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Another account claimed a slot while we were deciding. Read the new count and
                // try again — the retry may well find the coupon is now exhausted, which is the
                // correct answer rather than an error.
                _db.ChangeTracker.Clear();
                _logger.LogInformation(
                    "Retrying redemption of {Code} for business {UserId} after a concurrent redemption",
                    normalised, userId);
            }
        }

        // Every attempt lost. Rare enough to be worth saying plainly rather than retrying forever.
        throw ApiException.Conflict("That coupon is being redeemed by someone else. Please try again.");
    }

    /// <summary>How many times a redemption is retried when another account wins the race.</summary>
    private const int RedemptionAttempts = 4;

    private async Task<SubscriptionDto> TryRedeemAsync(Guid userId, string code, CancellationToken ct)
    {
        var subscription = await GetOrCreateAsync(userId, ct);
        var coupon = await _db.Coupons.FirstOrDefaultAsync(c => c.Code == code, ct);

        // Deliberately the same message for "no such code" and "this code is finished". A
        // different answer for each would let someone enumerate which codes exist.
        if (coupon is null || !coupon.IsRedeemable(DateTime.UtcNow))
            throw ApiException.BadRequest("That coupon code is not valid.");

        var now = DateTime.UtcNow;
        var trialEndBefore = subscription.TrialEnd;

        // Extended from whichever is later: a business with four months left does not lose them
        // by redeeming a six-month coupon, and one whose trial lapsed last week gets six months
        // from today rather than six months from a date already past.
        var from = trialEndBefore is not null && trialEndBefore > now ? trialEndBefore.Value : now;

        // AddMonths, not AddDays(30 * n). "Six months" means the same day six months later, which
        // is what a person redeeming this expects to see.
        subscription.TrialEnd = from.AddMonths(coupon.FreeMonths);
        subscription.TrialStart ??= now;
        subscription.UpdatedAt = now;

        // A business whose trial had already expired is genuinely back in trial.
        if (subscription.Status is SubscriptionStatus.Expired or SubscriptionStatus.Trialing)
        {
            subscription.Status = SubscriptionStatus.Trialing;
            subscription.StatusMessage = null;
        }

        var redemption = new CouponRedemption
        {
            Id = Guid.NewGuid(),
            CouponId = coupon.Id,
            UserId = userId,
            SubscriptionId = subscription.Id,
            FreeMonthsGranted = coupon.FreeMonths,
            TrialEndBefore = trialEndBefore,
            TrialEndAfter = subscription.TrialEnd,
            CodeUsed = code,
            RedeemedAt = now
        };

        _db.CouponRedemptions.Add(redemption);
        coupon.RedemptionCount += 1;
        coupon.UpdatedAt = now;
        // Rotating the token is what makes the increment safe. Without it the generated UPDATE
        // matches on the unchanged value and two writers both succeed, each having added one to
        // the count they read — so the coupon ends up one redemption short of the truth.
        coupon.ConcurrencyStamp = Guid.NewGuid();

        try
        {
            // One SaveChanges, so EF wraps the redemption row and the count increment in a single
            // implicit transaction. They cannot land separately.
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException)
        {
            _db.ChangeTracker.Clear();

            // The unique index on (CouponId, UserId) is what enforces one redemption per account,
            // and it is what just fired. An application-level "have they already?" check cannot
            // do this: two concurrent requests can both pass it.
            var already = await _db.CouponRedemptions.AsNoTracking()
                .AnyAsync(r => r.CouponId == coupon.Id && r.UserId == userId, ct);

            if (already)
                throw ApiException.Conflict("You have already used this coupon.");

            throw;
        }

        _logger.LogInformation(
            "Business {UserId} redeemed {Code} for {Months} free months, to {TrialEnd:yyyy-MM-dd}",
            userId, code, coupon.FreeMonths, subscription.TrialEnd);

        return Describe(subscription, redemption);
    }

    // ---- starting a subscription -----------------------------------------

    public async Task<SubscriptionCheckoutDto> StartAsync(Guid userId, CancellationToken ct = default)
    {
        if (!_options.Enabled)
            throw new ApiException(System.Net.HttpStatusCode.ServiceUnavailable,
                "Subscriptions are not available on this deployment yet.");

        if (!_provider.IsConfigured)
            throw new ApiException(System.Net.HttpStatusCode.ServiceUnavailable,
                "Subscriptions are not available at the moment. Please try again later.");

        var subscription = await GetOrCreateAsync(userId, ct);

        if (subscription.ProviderSubscriptionId is not null &&
            subscription.Status is SubscriptionStatus.Active or SubscriptionStatus.Trialing &&
            subscription.CancelRequestedAt is null)
        {
            throw ApiException.Conflict("You already have a subscription set up.");
        }

        var plan = _options.FindPlan(subscription.PlanCode) ?? _options.DefaultPlan;

        // The plan is a provider-side object we own, created once and reused. Cached on the
        // subscription row so the second subscriber does not create a second identical plan.
        var providerPlanId = subscription.ProviderPlanId
                             ?? await _db.Subscriptions
                                 .Where(s => s.PlanCode == plan.Code && s.ProviderPlanId != null)
                                 .Select(s => s.ProviderPlanId)
                                 .FirstOrDefaultAsync(ct)
                             ?? await _provider.EnsurePlanAsync(plan, ct);

        // THE trial, expressed to Razorpay. Billing starts when free access ends — including the
        // six months a QUOTELY6 redemption just granted, because that is what moved TrialEnd.
        var startAt = subscription.TrialEnd is not null && subscription.TrialEnd > DateTime.UtcNow
            ? subscription.TrialEnd
            : null;

        ProviderSubscription created;
        try
        {
            created = await _provider.CreateSubscriptionAsync(
                providerPlanId,
                totalCycles: null,
                startAt: startAt,
                notes: new Dictionary<string, string>
                {
                    // Our own id, so a Razorpay dashboard entry traces back here. Not the user's
                    // email or name: a provider's notes field is not a place for personal data.
                    ["quotelySubscriptionId"] = subscription.Id.ToString("N"),
                    ["quotelyPlanCode"] = plan.Code
                },
                ct);
        }
        catch (PaymentProviderException ex)
        {
            _logger.LogWarning(ex, "Could not create a subscription for business {UserId}", userId);
            throw new ApiException(System.Net.HttpStatusCode.BadGateway, ex.Message);
        }

        subscription.ProviderPlanId = providerPlanId;
        subscription.ProviderSubscriptionId = created.Id;
        subscription.CancelRequestedAt = null;
        subscription.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Created subscription {ProviderSubscriptionId} for business {UserId}, first charge {StartAt:yyyy-MM-dd}",
            created.Id, userId, startAt);

        return new SubscriptionCheckoutDto
        {
            KeyId = _provider.PublicKey,
            SubscriptionId = created.Id,
            PlanName = plan.Name,
            Price = plan.Price,
            Currency = plan.Currency,
            FirstChargeAt = startAt,
            ShortUrl = created.ShortUrl
        };
    }

    public async Task<SubscriptionDto> ConfirmAsync(
        Guid userId, ConfirmSubscriptionRequest request, CancellationToken ct = default)
    {
        var subscription = await GetOrCreateAsync(userId, ct);

        // The mandate must be the one we created for this business. Without this check a valid
        // signature from somebody else's subscription would confirm this one.
        if (subscription.ProviderSubscriptionId is null ||
            !string.Equals(subscription.ProviderSubscriptionId, request.RazorpaySubscriptionId, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Rejected subscription confirmation for business {UserId}: the subscription is not theirs", userId);
            throw ApiException.BadRequest("This subscription could not be confirmed.");
        }

        try
        {
            _provider.VerifySubscriptionSignature(
                request.RazorpaySubscriptionId, request.RazorpayPaymentId, request.RazorpaySignature);
        }
        catch (PaymentSignatureException ex)
        {
            _logger.LogWarning("Subscription signature check failed for business {UserId}: {Reason}", userId, ex.Message);
            throw ApiException.BadRequest("This subscription could not be confirmed.");
        }

        // Asked of the provider rather than inferred from the callback. The browser saying the
        // mandate succeeded is not the same as the mandate having succeeded.
        ProviderSubscription state;
        try
        {
            state = await _provider.GetSubscriptionAsync(request.RazorpaySubscriptionId, ct);
        }
        catch (PaymentProviderException ex)
        {
            _logger.LogWarning(ex, "Could not confirm subscription for business {UserId}", userId);
            throw new ApiException(System.Net.HttpStatusCode.BadGateway,
                "We could not confirm your subscription yet. It will update automatically once Razorpay confirms it.");
        }

        Apply(subscription, state, charged: false, paymentFailed: false);
        await _db.SaveChangesAsync(ct);

        return await GetStatusAsync(userId, ct);
    }

    // ---- cancelling -------------------------------------------------------

    public async Task<SubscriptionDto> CancelAsync(Guid userId, CancellationToken ct = default)
    {
        var subscription = await GetOrCreateAsync(userId, ct);

        if (subscription.ProviderSubscriptionId is null)
        {
            // Nothing was ever set up at the provider. Cancelling a trial means letting it run
            // out, which it will do on its own — there is nothing to call Razorpay about.
            subscription.CancelRequestedAt = DateTime.UtcNow;
            subscription.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return await GetStatusAsync(userId, ct);
        }

        try
        {
            // At cycle end, always. Cancelling immediately would take away days already paid for.
            var state = await _provider.CancelSubscriptionAsync(
                subscription.ProviderSubscriptionId, atCycleEnd: true, ct);

            Apply(subscription, state, charged: false, paymentFailed: false);
        }
        catch (PaymentProviderException ex)
        {
            _logger.LogWarning(ex, "Could not cancel the subscription for business {UserId}", userId);
            throw new ApiException(System.Net.HttpStatusCode.BadGateway,
                "We could not cancel your subscription just now. Please try again in a moment.");
        }

        subscription.CancelRequestedAt = DateTime.UtcNow;
        subscription.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Business {UserId} cancelled their subscription", userId);

        return await GetStatusAsync(userId, ct);
    }

    // ---- provider state ---------------------------------------------------

    public async Task ApplyProviderStateAsync(
        ProviderSubscription state, bool charged, bool paymentFailed, CancellationToken ct = default)
    {
        var subscription = await _db.Subscriptions
            .FirstOrDefaultAsync(s => s.ProviderSubscriptionId == state.Id, ct);

        if (subscription is null)
        {
            // A subscription at the provider that is not ours. Nothing to do, and nothing to
            // invent — we never create a local subscription from a webhook.
            _logger.LogWarning("Ignoring subscription event for unknown subscription {ProviderSubscriptionId}", state.Id);
            return;
        }

        Apply(subscription, state, charged, paymentFailed);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// The single place a subscription's state changes in response to the provider. Confirmation,
    /// cancellation and webhooks all come through here, so they cannot drift into disagreeing
    /// about what a given provider status means.
    /// </summary>
    private void Apply(
        Subscription subscription, ProviderSubscription state, bool charged, bool paymentFailed)
    {
        var now = DateTime.UtcNow;

        subscription.ProviderSubscriptionId = state.Id;
        subscription.CurrentPeriodStart = state.CurrentStart ?? subscription.CurrentPeriodStart;
        subscription.CurrentPeriodEnd = state.CurrentEnd ?? subscription.CurrentPeriodEnd;

        // A trial that is still running is not overwritten by the provider's idea of the status.
        // Razorpay reports a subscription with a future start_at as "authenticated", which we map
        // to Trialing — but a late event could otherwise walk a business out of a trial they are
        // in the middle of, and the trial end date is ours to know, not Razorpay's.
        var stillInTrial = subscription.TrialEnd is not null && subscription.TrialEnd > now;

        subscription.Status = state.Status switch
        {
            SubscriptionStatus.Trialing when !stillInTrial => SubscriptionStatus.Active,
            SubscriptionStatus.Active when stillInTrial => SubscriptionStatus.Trialing,
            _ => state.Status
        };

        if (charged)
        {
            subscription.LastPaymentAt = now;
            subscription.LastPaymentFailedAt = null;
            subscription.StatusMessage = null;
        }

        if (paymentFailed)
        {
            subscription.LastPaymentFailedAt = now;
            subscription.StatusMessage =
                "Your last payment did not go through. Please update your payment method.";
        }

        if (state.Status == SubscriptionStatus.Cancelled)
            subscription.CancelledAt = state.EndedAt ?? subscription.CancelledAt ?? now;

        subscription.UpdatedAt = now;

        _logger.LogInformation(
            "Subscription {ProviderSubscriptionId} for business {UserId} is now {Status} (provider: {ProviderStatus})",
            state.Id, subscription.UserId, subscription.Status, state.ProviderStatus);
    }
}
