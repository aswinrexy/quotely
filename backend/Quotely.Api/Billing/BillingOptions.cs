namespace Quotely.Api.Billing;

/// <summary>
/// What Quotely charges, how long the free period is, and what an expired subscription may still
/// do. All configuration, because every one of these is a commercial decision that should not
/// need a deployment to change — and because the gating rules in particular have to be adjustable
/// while the product is young.
/// </summary>
public class BillingOptions
{
    public const string SectionName = "Billing";

    /// <summary>
    /// Switches subscription billing on. Off by default, so a deployment that has not been set up
    /// for it behaves exactly as it did before rather than charging anyone by surprise.
    ///
    /// While this is false every business has full access and no subscription is ever created at
    /// the provider. Nothing is silently accruing in the background.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Whether the free-tier limits actually bite. Separate from <see cref="Enabled"/> so billing
    /// can be introduced — plans visible, subscriptions real — before the day anybody is stopped.
    ///
    /// CAUTION: turning this on without <see cref="Enabled"/> leaves a business that hits a limit
    /// with no way to pay their way past it. Startup validation refuses that combination in
    /// production rather than letting it happen quietly.
    /// </summary>
    public bool EnforceEntitlements { get; set; }

    /// <summary>
    /// Free days for a business that signs up without a coupon. Quotely's launch offer is the
    /// six-month coupon; this is the ordinary trial for everyone else.
    /// </summary>
    public int DefaultTrialDays { get; set; } = 14;

    /// <summary>
    /// How long past a failed payment a subscription keeps working before it expires. Razorpay
    /// retries on its own for several days, so expiring sooner than it gives up would lock out
    /// businesses whose payment was about to succeed.
    /// </summary>
    public int GraceDays { get; set; } = 14;

    /// <summary>The plan a new subscription is created on.</summary>
    public string DefaultPlanCode { get; set; } = "pro";

    public List<BillingPlanOptions> Plans { get; set; } = new()
    {
        new BillingPlanOptions
        {
            Code = "pro",
            Name = "Quotely Pro",
            Description = "Everything in Quotely, for one business.",
            Price = 150m,
            Currency = "INR",
            Interval = "monthly"
        }
    };

    /// <summary>
    /// What an account WITHOUT access may still do. Deliberately generous, and deliberately
    /// configurable: someone who stops paying does not stop owning their own records, and a
    /// product that holds a small business's invoice history hostage is not one worth building.
    /// </summary>
    public EntitlementOptions WithoutSubscription { get; set; } = new();

    public BillingPlanOptions? FindPlan(string code) =>
        Plans.FirstOrDefault(p => string.Equals(p.Code, code, StringComparison.OrdinalIgnoreCase));

    public BillingPlanOptions DefaultPlan =>
        FindPlan(DefaultPlanCode)
        ?? Plans.FirstOrDefault()
        ?? throw new InvalidOperationException("No billing plan is configured.");
}

public class BillingPlanOptions
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Currency { get; set; } = "INR";

    /// <summary>Razorpay's own vocabulary: daily, weekly, monthly or yearly.</summary>
    public string Interval { get; set; } = "monthly";

    public int IntervalCount { get; set; } = 1;
}

/// <summary>
/// What a business may do WITHOUT a paid subscription — the free tier.
///
/// Read by <c>ISubscriptionEntitlementService</c> and nowhere else. Every value is configuration
/// rather than a constant, because where the free tier stops is a commercial decision that should
/// not need a deployment to change.
///
/// The shape of the tier: the product is fully VISIBLE, and a business can run a real if small
/// operation on it — a handful of customers and a modest number of invoices, priced from a
/// catalogue it can build out as far as it likes. What it cannot do is quote, take card payments,
/// or grow past those counts.
///
/// The catalogue is deliberately not one of the counts. Every document line is priced from it, so
/// capping it would cap what a business can bill for at all rather than how much.
/// </summary>
public class EntitlementOptions
{
    /// <summary>Signing in is never withheld. Locking someone out of their own account is not a
    /// dunning strategy, and they cannot pay us from a login page they cannot pass.</summary>
    public bool CanSignIn { get; set; } = true;

    /// <summary>Reading what they already have. Their records, not ours.</summary>
    public bool CanViewExistingData { get; set; } = true;

    /// <summary>Getting their data out. Withholding this would be holding it hostage.</summary>
    public bool CanExportData { get; set; } = true;

    /// <summary>Reaching the billing page, which is how they would fix this.</summary>
    public bool CanManageBilling { get; set; } = true;

    // ---- what a free account cannot do at all ----

    /// <summary>Quotations are a paid feature. Invoicing is the free tier's whole job.</summary>
    public bool CanCreateQuotation { get; set; }

    /// <summary>
    /// Connecting a payment account is paid. A free business still gets paid — by cash, transfer
    /// or cheque, recorded manually — it just cannot put a Pay button on an invoice.
    /// </summary>
    public bool CanConnectPayments { get; set; }

    // ---- what a free account can do, up to a point ----

    /// <summary>
    /// Invoices a free account may create in total. Null means no limit.
    ///
    /// A total rather than a monthly allowance, deliberately: a monthly reset invites someone to
    /// wait out the calendar rather than decide, and it needs a clock nobody can see. A lifetime
    /// count is a number a person can hold in their head.
    /// </summary>
    public int? MaxInvoices { get; set; } = 10;

    public int? MaxCustomers { get; set; } = 5;

    /// <summary>
    /// Null: a free account may build as large a catalogue as it likes.
    ///
    /// This was 10, and capping it stopped making sense the moment every invoice and quotation
    /// line had to come from the catalogue. Free text used to be the escape hatch; with it gone, a
    /// business at the cap could not bill for an eleventh distinct thing AT ALL — not at a limit,
    /// not with a warning, simply unable to invoice work it had done. A limit that stops someone
    /// billing their customer does not sell subscriptions, it loses them.
    ///
    /// The catalogue is also not what Pro is worth paying for. Volume is: invoices and customers
    /// still carry allowances, and those grow with the business in a way a price list does not.
    /// Setting up a catalogue is the work a new account does before its first invoice, and taxing
    /// that is taxing the part we want them to finish.
    ///
    /// Still configurable, and the enforcement path is unchanged and still tested — this is a
    /// commercial decision, not a removed capability.
    /// </summary>
    public int? MaxProducts { get; set; }

    /// <summary>
    /// Whether an existing invoice can still be paid online. True on purpose: a customer settling
    /// an invoice they were sent last week is not the person whose subscription lapsed, and that
    /// money belongs to the business regardless of what they owe us.
    /// </summary>
    public bool CanAcceptPayments { get; set; } = true;
}
