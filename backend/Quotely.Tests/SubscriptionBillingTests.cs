using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Quotely.Api.Billing;
using Quotely.Api.Data;
using Quotely.Api.DTOs;
using Quotely.Api.Models;
using Xunit;

namespace Quotely.Tests;

/// <summary>
/// Quotely's OWN subscription billing: the ₹150/month businesses pay us, the QUOTELY6 coupon,
/// and the rules about what an account may do when it stops paying.
///
/// The property running underneath all of it: this money and merchant invoice money never touch.
/// </summary>
public class SubscriptionBillingTests : IClassFixture<QuotelyApiFactory>
{
    private readonly QuotelyApiFactory _factory;

    public SubscriptionBillingTests(QuotelyApiFactory factory) => _factory = factory;

    private static Task<SubscriptionDto?> GetAsync(HttpClient client) =>
        client.GetFromJsonAsync<SubscriptionDto>("/api/billing/subscription", QuotelyApiFactory.Json);

    private static Task<HttpResponseMessage> RedeemAsync(HttpClient client, string code) =>
        client.PostAsJsonAsync("/api/billing/coupon", new { code });

    // =====================================================================
    // 1. Every new business starts on a trial
    // =====================================================================

    [Fact]
    public async Task A_new_business_starts_on_a_trial_with_an_end_date()
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);

        var subscription = await GetAsync(client);

        subscription!.Status.Should().Be(SubscriptionStatus.Trialing);
        subscription.InTrial.Should().BeTrue();
        subscription.TrialEnd.Should().NotBeNull().And.BeAfter(DateTime.UtcNow);
        subscription.PlanName.Should().Be("Quotely Pro");
        subscription.Price.Should().Be(150m);
        subscription.Currency.Should().Be("INR");
        subscription.Interval.Should().Be("monthly");
        subscription.HasAccess.Should().BeTrue();
    }

    [Fact]
    public async Task Asking_twice_does_not_create_two_subscriptions()
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);

        var first = await GetAsync(client);
        var second = await GetAsync(client);

        // The trial started once; the second read must not have restarted it.
        second!.TrialStart.Should().Be(first!.TrialStart);
        second.TrialEnd.Should().Be(first.TrialEnd);
    }

    [Fact]
    public async Task The_subscription_never_exposes_provider_internals()
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);

        var body = await (await client.GetAsync("/api/billing/subscription")).Content.ReadAsStringAsync();

        body.Should().NotContain("sub_").And.NotContain("plan_").And.NotContain("rzp_");
        body.Should().NotContain("providerSubscriptionId").And.NotContain("providerPlanId");
    }

    // =====================================================================
    // 2. QUOTELY6
    // =====================================================================

    [Fact]
    public async Task The_six_month_coupon_extends_the_trial_by_six_calendar_months()
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);
        var before = await GetAsync(client);

        var response = await RedeemAsync(client, WellKnownCoupons.SixMonthsFree);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var after = (await response.Content.ReadFromJsonAsync<SubscriptionDto>(QuotelyApiFactory.Json))!;

        after.CouponCode.Should().Be("QUOTELY6");
        after.CouponFreeMonths.Should().Be(6);
        after.CouponRedeemedAt.Should().NotBeNull();
        after.Status.Should().Be(SubscriptionStatus.Trialing);

        // Six CALENDAR months from where the trial already ended — the same day six months later,
        // not 180 days, and not six months from today discarding the days already granted.
        after.TrialEnd.Should().Be(before!.TrialEnd!.Value.AddMonths(6));

        // …and the first payment moves with it.
        after.NextPaymentAt.Should().Be(after.TrialEnd);
    }

    [Fact]
    public async Task The_coupon_is_matched_without_regard_to_case_or_spacing()
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);

        (await RedeemAsync(client, "  quotely6  ")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await GetAsync(client))!.CouponCode.Should().Be("QUOTELY6");
    }

    [Fact]
    public async Task One_account_cannot_redeem_the_same_coupon_twice()
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);

        (await RedeemAsync(client, WellKnownCoupons.SixMonthsFree)).StatusCode.Should().Be(HttpStatusCode.OK);
        var afterFirst = await GetAsync(client);

        var second = await RedeemAsync(client, WellKnownCoupons.SixMonthsFree);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // And the trial was not extended a second time.
        (await GetAsync(client))!.TrialEnd.Should().Be(afterFirst!.TrialEnd);
    }

    [Fact]
    public async Task Concurrent_redemptions_of_the_same_coupon_grant_it_exactly_once()
    {
        // The reason "once per account" is a unique index rather than an if-statement: both of
        // these requests can pass an "already redeemed?" check, but only one can win the insert.
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);
        var before = await GetAsync(client);

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 4).Select(_ => RedeemAsync(client, WellKnownCoupons.SixMonthsFree)));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        responses.Where(r => r.StatusCode != HttpStatusCode.OK)
            .Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Conflict);

        // Six months granted, not twenty-four.
        (await GetAsync(client))!.TrialEnd.Should().Be(before!.TrialEnd!.Value.AddMonths(6));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var subscription = await db.Subscriptions.AsNoTracking()
            .OrderByDescending(s => s.UpdatedAt).FirstAsync();

        (await db.CouponRedemptions.CountAsync(r => r.SubscriptionId == subscription.Id))
            .Should().Be(1);
    }

    [Fact]
    public async Task Different_accounts_may_each_redeem_the_coupon()
    {
        var alice = await _factory.CreateSignedInClientAsync(connectPayments: false);
        var bob = await _factory.CreateSignedInClientAsync(connectPayments: false);

        (await RedeemAsync(alice, WellKnownCoupons.SixMonthsFree)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await RedeemAsync(bob, WellKnownCoupons.SixMonthsFree)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await GetAsync(alice))!.CouponFreeMonths.Should().Be(6);
        (await GetAsync(bob))!.CouponFreeMonths.Should().Be(6);
    }

    [Theory]
    [InlineData("NOPE")]
    [InlineData("QUOTELY12")]
    [InlineData("quotely-6")]
    public async Task An_unknown_coupon_is_refused(string code)
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);
        var before = await GetAsync(client);

        var response = await RedeemAsync(client, code);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        // The same message whatever is wrong with it, so the endpoint cannot be used to discover
        // which codes exist.
        (await response.Content.ReadAsStringAsync()).Should().Contain("not valid");

        (await GetAsync(client))!.TrialEnd.Should().Be(before!.TrialEnd);
    }

    [Fact]
    public async Task A_withdrawn_or_expired_coupon_is_refused()
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);

        var expired = await AddCouponAsync("EXPIRED6", freeMonths: 6, expiresAt: DateTime.UtcNow.AddDays(-1));
        var withdrawn = await AddCouponAsync("WITHDRAWN6", freeMonths: 6, isActive: false);
        var usedUp = await AddCouponAsync("ONLYONE", freeMonths: 1, maxRedemptions: 0);

        foreach (var code in new[] { expired, withdrawn, usedUp })
            (await RedeemAsync(client, code)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_limited_coupon_stops_at_its_limit()
    {
        var code = await AddCouponAsync("ONLYTWO", freeMonths: 1, maxRedemptions: 2);

        var clients = new List<HttpClient>();
        for (var i = 0; i < 4; i++)
            clients.Add(await _factory.CreateSignedInClientAsync(connectPayments: false));

        var responses = await Task.WhenAll(clients.Select(c => RedeemAsync(c, code)));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(2);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var coupon = await db.Coupons.AsNoTracking().FirstAsync(c => c.Code == code);

        coupon.RedemptionCount.Should().Be(2, "the count and the redemptions are written in one transaction");
        (await db.CouponRedemptions.CountAsync(r => r.CouponId == coupon.Id)).Should().Be(2);
    }

    [Fact]
    public async Task The_redemption_records_what_it_actually_granted()
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);
        var before = await GetAsync(client);

        await RedeemAsync(client, WellKnownCoupons.SixMonthsFree);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var redemption = await db.CouponRedemptions.AsNoTracking()
            .OrderByDescending(r => r.RedeemedAt).FirstAsync();

        redemption.FreeMonthsGranted.Should().Be(6);
        redemption.CodeUsed.Should().Be("QUOTELY6");
        // Before and after, so the effect can be reconstructed from the audit trail alone.
        redemption.TrialEndBefore.Should().BeCloseTo(before!.TrialEnd!.Value, TimeSpan.FromSeconds(1));
        redemption.TrialEndAfter.Should().BeCloseTo(before.TrialEnd.Value.AddMonths(6), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Redeeming_requires_a_signed_in_owner()
    {
        var anonymous = _factory.CreateClient();

        (await anonymous.PostAsJsonAsync("/api/billing/coupon", new { code = "QUOTELY6" }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync("/api/billing/subscription"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // =====================================================================
    // 3. Starting a subscription is off until billing is switched on
    // =====================================================================

    [Fact]
    public async Task Starting_a_subscription_is_unavailable_while_billing_is_disabled()
    {
        // Billing is off by default. A deployment that has not decided to charge anybody must not
        // create a mandate at the provider by surprise.
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);

        (await client.PostAsync("/api/billing/subscription", null))
            .StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        (await GetAsync(client))!.BillingEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Cancelling_a_trial_needs_no_provider_call()
    {
        // Nothing was ever set up at Razorpay during a trial, so cancelling one is a local fact.
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);

        var response = await client.DeleteAsync("/api/billing/subscription");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var subscription = (await response.Content.ReadFromJsonAsync<SubscriptionDto>(QuotelyApiFactory.Json))!;
        subscription.CancelRequestedAt.Should().NotBeNull();
        // Still theirs until the trial runs out.
        subscription.HasAccess.Should().BeTrue();
    }

    // =====================================================================
    // 4. Entitlements
    // =====================================================================

    [Fact]
    public async Task A_trial_can_do_everything()
    {
        // Enforcement is off by default and a trial grants access anyway, so both reasons point
        // the same way. This asserts the product is usable, which is the state it ships in.
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);

        var customer = (await (await client.PostAsJsonAsync("/api/customers", new
        {
            name = "Trial Customer", email = "t@example.com", city = "Chennai"
        })).Content.ReadFromJsonAsync<CustomerDto>(QuotelyApiFactory.Json))!;

        var quotation = await client.PostAsJsonAsync("/api/quotations", new
        {
            customerId = customer.Id,
            quotationDate = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
            validUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(15)).ToString("yyyy-MM-dd"),
            items = new object[]
            {
                new { name = "Work", unit = "Service", quantity = 1, unitPrice = 100, discount = 0, taxRate = 0 }
            }
        });

        quotation.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task The_entitlement_rules_describe_a_trial_as_fully_enabled()
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entitlements = scope.ServiceProvider.GetRequiredService<ISubscriptionEntitlementService>();

        var subscription = await db.Subscriptions.AsNoTracking().OrderByDescending(s => s.CreatedAt).FirstAsync();
        var summary = await entitlements.DescribeAsync(subscription.UserId);

        summary.HasAccess.Should().BeTrue();
        summary.CanCreateQuotation.Should().BeTrue();
        summary.CanCreateInvoice.Should().BeTrue();
        summary.CanExportData.Should().BeTrue();
    }

    [Theory]
    [InlineData(SubscriptionStatus.Trialing, true)]
    [InlineData(SubscriptionStatus.Active, true)]
    // A failed charge is not a reason to take someone's invoices away the same hour. Razorpay
    // retries on its own for days, and a webhook is sometimes simply late.
    [InlineData(SubscriptionStatus.PastDue, true)]
    [InlineData(SubscriptionStatus.Expired, false)]
    public void Access_follows_the_status_and_is_lenient_about_a_failed_charge(
        SubscriptionStatus status, bool expected)
    {
        var now = DateTime.UtcNow;
        var subscription = new Subscription
        {
            Status = status,
            TrialEnd = now.AddDays(5),
            CurrentPeriodEnd = now.AddDays(5)
        };

        subscription.GrantsAccess(now).Should().Be(expected);
    }

    [Fact]
    public void A_cancelled_subscription_keeps_working_until_the_paid_period_ends()
    {
        var now = DateTime.UtcNow;

        new Subscription { Status = SubscriptionStatus.Cancelled, CurrentPeriodEnd = now.AddDays(12) }
            .GrantsAccess(now).Should().BeTrue("they paid for those days");

        new Subscription { Status = SubscriptionStatus.Cancelled, CurrentPeriodEnd = now.AddDays(-1) }
            .GrantsAccess(now).Should().BeFalse();
    }

    [Fact]
    public void A_trial_that_has_run_out_no_longer_grants_access()
    {
        var now = DateTime.UtcNow;

        new Subscription { Status = SubscriptionStatus.Trialing, TrialEnd = now.AddSeconds(-1) }
            .GrantsAccess(now).Should().BeFalse();
    }

    // =====================================================================
    // 5. The two money flows do not meet
    // =====================================================================

    [Fact]
    public async Task A_merchants_webhook_secret_cannot_move_a_subscription()
    {
        // The whole separation, as one request. A business with its own Razorpay connection signs
        // a subscription event with ITS secret and delivers it to Quotely's billing endpoint.
        // Quotely's own secret is what verifies there, so this must be refused.
        var client = await _factory.CreateSignedInClientAsync();
        var merchantSecret = _factory.ConnectionFor(client).WebhookSecret;

        const string body = "{\"event\":\"subscription.charged\",\"payload\":{\"subscription\":{\"entity\":" +
                            "{\"id\":\"sub_forged\",\"status\":\"active\"}}}}";

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/razorpay/billing")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Razorpay-Signature", FakePaymentProvider.Sign(body, merchantSecret));
        request.Headers.Add("X-Razorpay-Event-Id", "evt_forged_billing");

        var response = await _factory.CreateClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_subscription_event_for_an_unknown_subscription_changes_nothing()
    {
        const string body = "{\"event\":\"subscription.charged\",\"payload\":{\"subscription\":{\"entity\":" +
                            "{\"id\":\"sub_not_ours\",\"status\":\"active\"}}}}";

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/razorpay/billing")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add(
            "X-Razorpay-Signature", FakePaymentProvider.Sign(body, QuotelyApiFactory.PlatformWebhookSecret));
        request.Headers.Add("X-Razorpay-Event-Id", "evt_unknown_sub");

        var response = await _factory.CreateClient().SendAsync(request);

        // Acknowledged so Razorpay stops retrying; we never invent a local subscription from an
        // event about one we have no record of.
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Subscriptions.AnyAsync(s => s.ProviderSubscriptionId == "sub_not_ours")).Should().BeFalse();
    }

    [Fact]
    public async Task A_billing_event_and_a_merchant_event_can_share_an_id()
    {
        // Both come from Razorpay and both land in one WebhookEvents table. The provider
        // discriminator is what keeps one from being mistaken for a duplicate of the other.
        var client = await _factory.CreateSignedInClientAsync();
        var merchant = _factory.ConnectionFor(client);

        const string sharedId = "evt_both_flows_use_this";
        var merchantBody = FakePaymentProvider.WebhookBody(
            "payment.captured", "pay_shared_id", "order_none", "captured", 100);

        var merchantRequest = new HttpRequestMessage(HttpMethod.Post, merchant.WebhookUrl)
        {
            Content = new StringContent(merchantBody, Encoding.UTF8, "application/json")
        };
        merchantRequest.Headers.Add(
            "X-Razorpay-Signature", FakePaymentProvider.Sign(merchantBody, merchant.WebhookSecret));
        merchantRequest.Headers.Add("X-Razorpay-Event-Id", sharedId);
        (await _factory.CreateClient().SendAsync(merchantRequest)).StatusCode.Should().Be(HttpStatusCode.OK);

        const string billingBody = "{\"event\":\"subscription.updated\",\"payload\":{}}";
        var billingRequest = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/razorpay/billing")
        {
            Content = new StringContent(billingBody, Encoding.UTF8, "application/json")
        };
        billingRequest.Headers.Add(
            "X-Razorpay-Signature",
            FakePaymentProvider.Sign(billingBody, QuotelyApiFactory.PlatformWebhookSecret));
        billingRequest.Headers.Add("X-Razorpay-Event-Id", sharedId);

        // Accepted on its own merits, not discarded as a duplicate of the merchant one.
        (await _factory.CreateClient().SendAsync(billingRequest)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.WebhookEvents.CountAsync(e => e.EventId.EndsWith(sharedId))).Should().Be(2);
    }

    [Fact]
    public async Task A_redelivered_billing_event_is_acknowledged_once()
    {
        const string body = "{\"event\":\"subscription.updated\",\"payload\":{}}";

        async Task<HttpResponseMessage> Deliver() =>
            await _factory.CreateClient().SendAsync(Build());

        HttpRequestMessage Build()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/razorpay/billing")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            request.Headers.Add(
                "X-Razorpay-Signature",
                FakePaymentProvider.Sign(body, QuotelyApiFactory.PlatformWebhookSecret));
            request.Headers.Add("X-Razorpay-Event-Id", "evt_billing_repeat");
            return request;
        }

        (await Deliver()).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Deliver()).StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.WebhookEvents.CountAsync(e => e.EventId == "evt_billing_repeat")).Should().Be(1);
    }

    [Fact]
    public async Task An_unsigned_billing_webhook_is_rejected()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/razorpay/billing")
        {
            Content = new StringContent(
                "{\"event\":\"subscription.charged\"}", Encoding.UTF8, "application/json")
        };

        (await _factory.CreateClient().SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---- helpers ---------------------------------------------------------

    private async Task<string> AddCouponAsync(
        string code, int freeMonths, int? maxRedemptions = null,
        DateTime? expiresAt = null, bool isActive = true)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (!await db.Coupons.AnyAsync(c => c.Code == code))
        {
            db.Coupons.Add(new Coupon
            {
                Id = Guid.NewGuid(),
                Code = code,
                Description = $"{freeMonths} free months, for a test.",
                FreeMonths = freeMonths,
                MaxRedemptions = maxRedemptions,
                ExpiresAt = expiresAt,
                IsActive = isActive
            });
            await db.SaveChangesAsync();
        }

        return code;
    }
}
