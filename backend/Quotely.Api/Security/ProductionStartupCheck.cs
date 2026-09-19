using Quotely.Api.Models;
using Quotely.Api.Payments;

namespace Quotely.Api.Security;

/// <summary>
/// What production refuses to start without.
///
/// Every one of these checks guards against a deployment that would appear to work while being
/// wrong about money or identity: a weak signing key, live credentials that are really test ones,
/// a merchant-credential store with no encryption key, an origin allow-list that is a wildcard.
/// Each of those fails silently at runtime, days later, in a way that is expensive to undo.
///
/// The checks run in production only. Development boots with placeholders on purpose, because a
/// developer who cannot start the application cannot fix it.
/// </summary>
public static class ProductionStartupCheck
{
    /// <summary>
    /// Throws with every problem listed at once, rather than one per restart. A deployment that
    /// is wrong in three ways should say so the first time.
    /// </summary>
    public static void Validate(
        IConfiguration configuration,
        IHostEnvironment environment,
        RazorpayOptions razorpay,
        EncryptionOptions encryption,
        string[] corsOrigins,
        Billing.BillingOptions? billing = null)
    {
        if (!environment.IsProduction()) return;

        var problems = new List<string>();

        // ---- signing ----
        var jwtKey = configuration["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32)
            problems.Add("JWT__KEY must be at least 32 characters.");

        // ---- database ----
        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("DefaultConnection")))
            problems.Add("ConnectionStrings__DefaultConnection is not set.");

        // Applying schema changes on start-up means an unreviewed migration runs against real
        // customer data the moment a container restarts. It is a development convenience.
        if (configuration.GetValue("Database:AutoMigrate", false))
            problems.Add("Database__AutoMigrate must be false in production.");

        // The demo seeder creates an account with a known password and writes that password to
        // the log. Harmless in development, and a published credential in production.
        if (configuration.GetValue("Seed:Enabled", false))
            problems.Add("Seed__Enabled must be false in production.");

        // ---- merchant credential encryption ----
        // Without a key, merchants cannot connect a payment account at all. Better to refuse the
        // deployment than to run one where a core feature fails at the moment it is used.
        if (encryption.PrimaryKeyId is null)
            problems.Add("Encryption__Key must be set to a base64-encoded 32-byte key (openssl rand -base64 32).");

        // ---- Quotely's own billing account ----
        if (razorpay.Mode == PaymentEnvironment.Live)
        {
            // NEVER a silent fall back to test. A live deployment that quietly runs on test keys
            // would take subscriptions that never arrive, and nobody would notice for a month.
            if (!razorpay.IsConfigured)
                problems.Add("Razorpay__Mode is Live but Razorpay__KeyId/Razorpay__KeySecret are not both set.");

            if (!razorpay.WebhooksConfigured)
                problems.Add("Razorpay__Mode is Live but Razorpay__WebhookSecret is not set.");

            if (!razorpay.ModeMatchesCredentials())
                problems.Add("Razorpay__Mode is Live but Razorpay__KeyId is not a live key (rzp_live_…).");
        }
        else if (!razorpay.ModeMatchesCredentials())
        {
            // The dangerous direction: real credentials in a deployment that believes it is
            // testing. Refused rather than warned about.
            problems.Add("Razorpay__KeyId is a live key but Razorpay__Mode is not Live.");
        }

        // ---- billing ----
        // Enforcing limits without a way to pay past them traps a business at the tenth invoice
        // with a button that cannot work. One switch without the other is a configuration error.
        if (billing is { EnforceEntitlements: true, Enabled: false })
            problems.Add("Billing__EnforceEntitlements is true but Billing__Enabled is false — a business that hits a limit would have no way to upgrade.");

        // ---- the browser's side ----
        if (corsOrigins.Length == 0)
            problems.Add("Cors__AllowedOrigins__0 must name the site's origin.");

        if (corsOrigins.Any(o => o.Contains('*')))
            problems.Add("Cors__AllowedOrigins must not contain a wildcard.");

        if (corsOrigins.Any(o => o.StartsWith("http://", StringComparison.OrdinalIgnoreCase)))
            problems.Add("Cors__AllowedOrigins must be https:// in production.");

        var publicBase = configuration["PublicLinks:BaseUrl"];
        if (string.IsNullOrWhiteSpace(publicBase))
            problems.Add("PublicLinks__BaseUrl must be set to the customer-facing site origin.");
        else if (!publicBase.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            problems.Add("PublicLinks__BaseUrl must be https:// in production.");

        if (problems.Count == 0) return;

        // The names of the settings, never their values. A start-up failure is written to a log
        // that is read by more people than the secret store is.
        throw new InvalidOperationException(
            "This deployment is not configured for production:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, problems.Select(p => "  - " + p)));
    }
}
