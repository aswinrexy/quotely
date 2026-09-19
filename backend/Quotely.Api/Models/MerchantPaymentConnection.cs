namespace Quotely.Api.Models;

/// <summary>
/// One tenant's own payment account, and the reason money from their invoices reaches them
/// rather than us.
///
/// This row is the entire answer to "whose Razorpay collects this payment?". An invoice resolves
/// its tenant, the tenant resolves this connection, and the connection supplies the credentials
/// the order is created with. There is no path from an invoice to a globally configured account,
/// because there is no globally configured account for invoice payments any more.
///
/// Quotely's OWN Razorpay account — the one that collects SaaS subscriptions — is not represented
/// here at all. It lives in configuration, is reached through <c>ISaasBillingProvider</c>, and the
/// two must never meet: see docs/architecture.md, "Three identities".
///
/// Nothing in this row is a card number, a CVV, a UPI PIN or a bank credential. The secrets it
/// does hold are the merchant's Razorpay API secret or OAuth tokens, and every one of them is
/// stored encrypted — see <see cref="Quotely.Api.Security.ISecretProtector"/>.
/// </summary>
public class MerchantPaymentConnection
{
    public Guid Id { get; set; }

    /// <summary>The tenant this account belongs to. One live connection per tenant per provider.</summary>
    public Guid UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>"Razorpay" today. A string so a second provider needs no migration.</summary>
    public string Provider { get; set; } = PaymentProviders.Razorpay;

    public MerchantConnectionMode Mode { get; set; } = MerchantConnectionMode.KeyPair;

    public MerchantConnectionStatus Status { get; set; } = MerchantConnectionStatus.Disconnected;

    /// <summary>
    /// Test or Live. A connection is bound to the environment it was made in: a test key must
    /// never be used to collect real money, and a live key must never be exercised by a test.
    /// </summary>
    public PaymentEnvironment Environment { get; set; } = PaymentEnvironment.Test;

    /// <summary>
    /// The provider's identifier for the merchant's account — Razorpay's <c>acc_…</c>. Present
    /// for OAuth connections, where Razorpay tells us; null for key-pair connections, where the
    /// key id is the only identity we are given.
    /// </summary>
    public string? ProviderAccountId { get; set; }

    /// <summary>
    /// The publishable key handed to the customer's browser — <c>rzp_live_…</c> for a key pair,
    /// <c>rzp_live_oauth_…</c> for OAuth. Publishable by definition, so it is stored in the clear.
    /// </summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>Encrypted. The merchant's Razorpay key secret. Key-pair connections only.</summary>
    public string? KeySecretCipher { get; set; }

    /// <summary>Encrypted. OAuth connections only.</summary>
    public string? AccessTokenCipher { get; set; }

    /// <summary>Encrypted. Used to mint a new access token before the old one expires.</summary>
    public string? RefreshTokenCipher { get; set; }

    /// <summary>
    /// When the access token stops working. Razorpay issues them for about 91 days, so this is
    /// months away rather than minutes — but it does arrive, and a connection that passes it
    /// stops being able to create orders.
    /// </summary>
    public DateTime? AccessTokenExpiresAt { get; set; }

    /// <summary>
    /// Encrypted. The secret this merchant's webhooks are signed with. Distinct per connection:
    /// a signature valid for one merchant must not verify for another, which is what makes
    /// <see cref="WebhookRouteToken"/> safe to put in a URL.
    /// </summary>
    public string? WebhookSecretCipher { get; set; }

    /// <summary>
    /// The unguessable segment in this connection's webhook URL. A random 32-byte value, not the
    /// row id: an internal identifier must never appear in something we hand to a third party.
    ///
    /// It selects which secret to verify against and nothing more. Knowing it does not let anyone
    /// deliver a webhook we will act on, because the signature still has to match.
    /// </summary>
    public string WebhookRouteToken { get; set; } = string.Empty;

    /// <summary>
    /// SHA-256 of the one-time state value handed to Razorpay when an OAuth authorisation begins.
    /// The hash rather than the value, for the same reason the public link tokens are hashed: a
    /// database read must not yield something that can be replayed.
    ///
    /// Checking it on the callback is what stops an attacker completing the flow with an
    /// authorisation code obtained elsewhere — the CSRF defence Razorpay's `state` parameter exists for.
    /// </summary>
    public string? OauthStateHash { get; set; }

    /// <summary>
    /// When the pending authorisation stops being acceptable. A state value with no deadline is a
    /// CSRF token that never expires.
    /// </summary>
    public DateTime? OauthStateExpiresAt { get; set; }

    /// <summary>The merchant's own name for the account, shown in settings. Never a credential.</summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Why the connection last stopped working, in words we are willing to show the owner.
    /// Provider error text is logged, never stored here.
    /// </summary>
    public string? StatusMessage { get; set; }

    public DateTime? ConnectedAt { get; set; }
    public DateTime? DisconnectedAt { get; set; }

    /// <summary>When we last proved the credentials work by calling the provider with them.</summary>
    public DateTime? LastVerifiedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Guards against two simultaneous connect/refresh attempts clobbering each other.</summary>
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Whether this connection may be used to collect money right now. An expired OAuth token is
    /// not usable even though the row still says Connected — the refresh has to happen first.
    /// </summary>
    public bool IsUsable =>
        Status == MerchantConnectionStatus.Connected &&
        !string.IsNullOrWhiteSpace(PublicKey) &&
        (Mode != MerchantConnectionMode.Oauth || AccessTokenExpiresAt is null || AccessTokenExpiresAt > DateTime.UtcNow);

    /// <summary>
    /// True when the access token is close enough to expiry that we should renew it before the
    /// next charge rather than after the next failure.
    /// </summary>
    public bool NeedsRefresh(TimeSpan window) =>
        Mode == MerchantConnectionMode.Oauth &&
        AccessTokenExpiresAt is not null &&
        AccessTokenExpiresAt <= DateTime.UtcNow + window;
}

/// <summary>How a merchant authorised us to collect on their behalf.</summary>
public enum MerchantConnectionMode
{
    /// <summary>
    /// The merchant generated an API key in their own Razorpay dashboard and gave it to us. Works
    /// without any partner programme, which is why it exists: it is the mode that lets a business
    /// take payments today. The secret is encrypted at rest and never leaves the server.
    /// </summary>
    KeyPair = 0,

    /// <summary>
    /// The merchant pressed "Connect" and authorised Quotely at Razorpay, who handed us a scoped
    /// access token instead of their key secret. The better mode, and the default once Quotely is
    /// an approved Razorpay Technology Partner — see docs/razorpay-partner.md.
    /// </summary>
    Oauth = 1
}

/// <summary>Where a connection stands, as the owner would describe it.</summary>
public enum MerchantConnectionStatus
{
    /// <summary>No account attached, or the owner detached the one that was.</summary>
    Disconnected = 0,

    /// <summary>Working. Invoices belonging to this tenant can be paid online.</summary>
    Connected = 1,

    /// <summary>
    /// The credentials stopped working — revoked at Razorpay, rotated, or refused. Distinct from
    /// Disconnected because the owner did not do it and needs to be told.
    /// </summary>
    Error = 2,

    /// <summary>
    /// The OAuth access token lapsed and could not be renewed. Recoverable by reconnecting; kept
    /// apart from Error so the settings page can say the one useful thing rather than the vague one.
    /// </summary>
    Expired = 3,

    /// <summary>
    /// OAuth was started but the merchant has not come back from Razorpay yet. A connection in
    /// this state holds no credentials and cannot collect anything.
    /// </summary>
    Pending = 4
}

/// <summary>
/// Test money or real money. Recorded on the connection, checked against the deployment's own
/// mode, and never inferred: a key that merely looks like a test key is not evidence.
/// </summary>
public enum PaymentEnvironment
{
    Test = 0,
    Live = 1
}
