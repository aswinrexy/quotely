using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Quotely.Api.Data;
using Quotely.Api.DTOs;
using Quotely.Api.Middleware;
using Quotely.Api.Models;
using Quotely.Api.Payments;
using Quotely.Api.Security;

namespace Quotely.Api.Services;

public interface IMerchantConnectionService
{
    /// <summary>
    /// THE tenant-isolation boundary for payments. Returns the credentials belonging to
    /// <paramref name="userId"/> and to nobody else, decrypted for one operation.
    ///
    /// Every caller passes the tenant that owns the invoice being paid. There is no overload that
    /// takes a connection id, because accepting one would mean accepting a caller's claim about
    /// which merchant to use — and that claim is exactly what must never be trusted.
    /// </summary>
    Task<MerchantPaymentContext> ResolveAsync(Guid userId, CancellationToken ct = default);

    /// <summary>The same resolution, returning null instead of throwing when there is no usable connection.</summary>
    Task<MerchantPaymentContext?> TryResolveAsync(Guid userId, CancellationToken ct = default);

    /// <summary>What the owner's settings page shows. Contains no secret of any kind.</summary>
    Task<MerchantConnectionDto> GetStatusAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Attaches a Razorpay account from the merchant's own API key pair.</summary>
    Task<MerchantConnectionDto> ConnectKeyPairAsync(
        Guid userId, ConnectRazorpayKeysRequest request, CancellationToken ct = default);

    /// <summary>Begins the OAuth flow, returning where to send the merchant.</summary>
    Task<MerchantConnectionStartDto> StartOauthAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Completes the OAuth flow from Razorpay's callback.</summary>
    Task<MerchantConnectionDto> CompleteOauthAsync(
        Guid userId, string code, string state, CancellationToken ct = default);

    /// <summary>Detaches the account. Revokes at the provider where that is possible.</summary>
    Task<MerchantConnectionDto> DisconnectAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Finds the connection a webhook delivery was addressed to. Resolution is by route token
    /// only — never by anything inside the unverified payload.
    /// </summary>
    Task<MerchantPaymentConnection?> FindByWebhookRouteAsync(string routeToken, CancellationToken ct = default);

    /// <summary>Decrypts a connection's webhook secret, or null when it has none.</summary>
    string? ReadWebhookSecret(MerchantPaymentConnection connection);

    /// <summary>Records that a connection's credentials were refused, so the owner is told.</summary>
    Task MarkUnusableAsync(Guid connectionId, string reason, CancellationToken ct = default);
}

/// <summary>
/// Owns merchant payment connections: how one is made, how it is read back, and — most
/// importantly — the single method through which credentials are handed to the payment code.
///
/// The security property this file exists to hold: a <see cref="MerchantPaymentContext"/> can
/// only be produced from a tenant id, and the tenant id only ever comes from a validated JWT or
/// from the invoice's own <c>UserId</c> column. There is no route by which a request can nominate
/// which merchant account collects its money.
/// </summary>
public class MerchantConnectionService : IMerchantConnectionService
{
    private readonly AppDbContext _db;
    private readonly ISecretProtector _protector;
    private readonly IMerchantPaymentProvider _provider;
    private readonly IRazorpayOauthClient _oauth;
    private readonly RazorpayOptions _options;
    private readonly ILogger<MerchantConnectionService> _logger;

    public MerchantConnectionService(
        AppDbContext db,
        ISecretProtector protector,
        IMerchantPaymentProvider provider,
        IRazorpayOauthClient oauth,
        IOptions<RazorpayOptions> options,
        ILogger<MerchantConnectionService> logger)
    {
        _db = db;
        _protector = protector;
        _provider = provider;
        _oauth = oauth;
        _options = options.Value;
        _logger = logger;
    }

    // ---- resolution -----------------------------------------------------

    public async Task<MerchantPaymentContext> ResolveAsync(Guid userId, CancellationToken ct = default)
    {
        var context = await TryResolveAsync(userId, ct);

        if (context is null)
            throw new ApiException(System.Net.HttpStatusCode.ServiceUnavailable,
                "This business has not set up online payments yet. Please contact them to arrange payment.");

        return context;
    }

    public async Task<MerchantPaymentContext?> TryResolveAsync(Guid userId, CancellationToken ct = default)
    {
        var connection = await _db.MerchantPaymentConnections
            .FirstOrDefaultAsync(c => c.UserId == userId && c.Provider == PaymentProviders.Razorpay, ct);

        if (connection is null || !connection.IsUsable) return null;

        // A live connection must not be exercised by a deployment that thinks it is in test mode,
        // and test credentials must never be reached for real money. The environments have to
        // agree, and a disagreement is a configuration error rather than something to work around.
        if (connection.Environment != _options.Mode)
        {
            _logger.LogError(
                "Connection {ConnectionId} is a {ConnectionEnvironment} account but this deployment runs in {DeploymentMode}",
                connection.Id, connection.Environment, _options.Mode);
            return null;
        }

        if (connection.NeedsRefresh(TimeSpan.FromDays(_options.Oauth.RefreshWindowDays)))
            connection = await RefreshAsync(connection, ct) ?? connection;

        if (!connection.IsUsable) return null;

        return BuildContext(connection);
    }

    /// <summary>
    /// The only place a <see cref="MerchantPaymentContext"/> is constructed. Decryption happens
    /// here and the plaintext goes no further than the returned record.
    /// </summary>
    private MerchantPaymentContext BuildContext(MerchantPaymentConnection connection) => new()
    {
        ConnectionId = connection.Id,
        UserId = connection.UserId,
        Provider = connection.Provider,
        Environment = connection.Environment,
        Credentials = new MerchantCredentials
        {
            Mode = connection.Mode,
            PublicKey = connection.PublicKey,
            KeySecret = Decrypt(connection.KeySecretCipher),
            AccessToken = Decrypt(connection.AccessTokenCipher),
            ProviderAccountId = connection.ProviderAccountId
        }
    };

    private string? Decrypt(string? cipher)
    {
        if (string.IsNullOrWhiteSpace(cipher)) return null;

        try
        {
            return _protector.Unprotect(cipher);
        }
        catch (SecretProtectionException ex)
        {
            // Almost always a rotated encryption key with no re-encryption pass. Loud, because
            // the alternative is a merchant's payments quietly failing with no stated cause.
            _logger.LogError(ex, "A stored merchant credential could not be decrypted");
            return null;
        }
    }

    // ---- status ---------------------------------------------------------

    public async Task<MerchantConnectionDto> GetStatusAsync(Guid userId, CancellationToken ct = default)
    {
        var connection = await _db.MerchantPaymentConnections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.UserId == userId && c.Provider == PaymentProviders.Razorpay, ct);

        return Describe(connection);
    }

    /// <summary>
    /// Maps a connection to what the owner may see. Nothing secret crosses this boundary: no key
    /// secret, no access token, no refresh token, no webhook secret, and not the row id either.
    /// </summary>
    private MerchantConnectionDto Describe(MerchantPaymentConnection? connection)
    {
        var oauthAvailable = _oauth.IsConfigured;

        if (connection is null)
        {
            return new MerchantConnectionDto
            {
                Provider = PaymentProviders.Razorpay,
                Status = MerchantConnectionStatus.Disconnected,
                OauthAvailable = oauthAvailable,
                KeyPairAvailable = true,
                Environment = _options.Mode
            };
        }

        return new MerchantConnectionDto
        {
            Provider = connection.Provider,
            Status = connection.Status,
            Mode = connection.Mode,
            Environment = connection.Environment,
            // The publishable key, truncated. Enough for an owner to tell which account is
            // attached; not the whole value, because a settings page is not the place to display
            // credentials in full even publishable ones.
            AccountLabel = BuildAccountLabel(connection),
            DisplayName = connection.DisplayName,
            StatusMessage = connection.StatusMessage,
            ConnectedAt = connection.ConnectedAt,
            DisconnectedAt = connection.DisconnectedAt,
            LastVerifiedAt = connection.LastVerifiedAt,
            AccessTokenExpiresAt = connection.AccessTokenExpiresAt,
            CanAcceptPayments = connection.IsUsable && connection.Environment == _options.Mode,
            OauthAvailable = oauthAvailable,
            KeyPairAvailable = true,
            WebhookUrl = BuildWebhookUrl(connection)
        };
    }

    private static string? BuildAccountLabel(MerchantPaymentConnection connection)
    {
        if (!string.IsNullOrWhiteSpace(connection.ProviderAccountId)) return connection.ProviderAccountId;
        if (string.IsNullOrWhiteSpace(connection.PublicKey)) return null;

        return connection.PublicKey.Length <= 12
            ? connection.PublicKey
            : connection.PublicKey[..12] + "…";
    }

    /// <summary>
    /// The URL a key-pair merchant pastes into their own Razorpay dashboard. Contains the route
    /// token, which is unguessable and merchant-specific, and nothing else about them.
    /// </summary>
    private string? BuildWebhookUrl(MerchantPaymentConnection connection) =>
        string.IsNullOrWhiteSpace(connection.WebhookRouteToken)
            ? null
            : $"/api/webhooks/razorpay/m/{connection.WebhookRouteToken}";

    // ---- connecting: key pair -------------------------------------------

    public async Task<MerchantConnectionDto> ConnectKeyPairAsync(
        Guid userId, ConnectRazorpayKeysRequest request, CancellationToken ct = default)
    {
        EnsureEncryptionAvailable();

        var keyId = request.KeyId.Trim();
        var keySecret = request.KeySecret.Trim();

        if (!keyId.StartsWith("rzp_", StringComparison.Ordinal))
            throw ApiException.BadRequest("That does not look like a Razorpay Key ID. It should begin with rzp_.");

        // A live key in a test deployment would collect real money through a system nobody has
        // signed off for it; a test key in production would take payments that never arrive.
        var keyEnvironment = keyId.StartsWith("rzp_live_", StringComparison.Ordinal)
            ? PaymentEnvironment.Live
            : PaymentEnvironment.Test;

        if (keyEnvironment != _options.Mode)
            throw ApiException.BadRequest(
                keyEnvironment == PaymentEnvironment.Live
                    ? "This is a live Razorpay key, but Quotely is running in test mode here. Use your test key."
                    : "This is a test Razorpay key, but Quotely is running in live mode here. Use your live key.");

        var connection = await LoadOrCreateAsync(userId, ct);

        // Proved before it is stored. The merchant finds out about a typo now, not when their
        // first customer cannot pay.
        var probe = await ProbeCredentialsAsync(new MerchantPaymentContext
        {
            ConnectionId = connection.Id,
            UserId = userId,
            Provider = PaymentProviders.Razorpay,
            Environment = keyEnvironment,
            Credentials = new MerchantCredentials
            {
                Mode = MerchantConnectionMode.KeyPair,
                PublicKey = keyId,
                KeySecret = keySecret
            }
        }, ct);

        connection.Mode = MerchantConnectionMode.KeyPair;
        connection.Environment = keyEnvironment;
        connection.PublicKey = keyId;
        connection.KeySecretCipher = _protector.Protect(keySecret);
        connection.ProviderAccountId = probe.ProviderAccountId;
        connection.DisplayName = string.IsNullOrWhiteSpace(request.DisplayName)
            ? probe.DisplayName
            : request.DisplayName.Trim();

        // Tokens from a previous OAuth connection are not merely unused, they are removed: a
        // credential we no longer need is a credential we should not be holding.
        connection.AccessTokenCipher = null;
        connection.RefreshTokenCipher = null;
        connection.AccessTokenExpiresAt = null;
        connection.OauthStateHash = null;
        connection.OauthStateExpiresAt = null;

        // A key-pair merchant configures their own webhook, so they need a secret to put in both
        // places. Generated here, shown once by the controller, stored encrypted.
        var webhookSecret = PublicTokenGenerator.CreateToken();
        connection.WebhookSecretCipher = _protector.Protect(webhookSecret);

        MarkConnected(connection);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two connect requests for the same business arrived together and the unique index on
            // (UserId, Provider) settled it. A 409 rather than a 500: nothing is wrong, one of the
            // two simply lost, and the winner's connection is the one that stands.
            _db.ChangeTracker.Clear();
            _logger.LogInformation(
                "A concurrent Razorpay connection for merchant {UserId} was already saved", userId);
            throw ApiException.Conflict(
                "This account was connected by another request. Reload the page to see it.");
        }

        _logger.LogInformation(
            "Merchant {UserId} connected a Razorpay account in {Environment} mode via key pair",
            userId, keyEnvironment);

        var dto = Describe(connection);
        // The only moment this value exists outside the database. Shown once, exactly like a
        // public share link, because it cannot be recovered afterwards.
        dto.WebhookSecret = webhookSecret;
        return dto;
    }

    // ---- connecting: oauth ----------------------------------------------

    public async Task<MerchantConnectionStartDto> StartOauthAsync(Guid userId, CancellationToken ct = default)
    {
        EnsureEncryptionAvailable();

        if (!_oauth.IsConfigured)
            throw new ApiException(System.Net.HttpStatusCode.ServiceUnavailable,
                "Connecting with Razorpay is not available yet. You can connect using your Razorpay API keys instead.");

        var connection = await LoadOrCreateAsync(userId, ct);

        // A fresh state value on every attempt, stored as a hash with a deadline.
        var state = PublicTokenGenerator.CreateToken();
        connection.OauthStateHash = PublicTokenGenerator.Hash(state);
        connection.OauthStateExpiresAt =
            DateTime.UtcNow.AddMinutes(_options.Oauth.AuthorizationTimeoutMinutes);
        connection.Mode = MerchantConnectionMode.Oauth;
        connection.Environment = _options.Mode;

        // Pending means "holds no credentials and can collect nothing". An in-flight
        // authorisation must not leave a previously working connection looking broken, so a
        // connection that is already live keeps its status until the new one actually completes.
        if (connection.Status != MerchantConnectionStatus.Connected)
            connection.Status = MerchantConnectionStatus.Pending;

        connection.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return new MerchantConnectionStartDto(_oauth.BuildAuthorizationUrl(state), state);
    }

    public async Task<MerchantConnectionDto> CompleteOauthAsync(
        Guid userId, string code, string state, CancellationToken ct = default)
    {
        EnsureEncryptionAvailable();

        var connection = await _db.MerchantPaymentConnections
            .FirstOrDefaultAsync(c => c.UserId == userId && c.Provider == PaymentProviders.Razorpay, ct)
            ?? throw ApiException.BadRequest("There is no Razorpay connection in progress for this account.");

        // The CSRF check. Compared as a hash of what was presented against the hash we stored,
        // and only within the window the authorisation was started in.
        if (string.IsNullOrWhiteSpace(connection.OauthStateHash) ||
            connection.OauthStateExpiresAt is null ||
            connection.OauthStateExpiresAt <= DateTime.UtcNow ||
            !PublicTokenGenerator.Matches(state, connection.OauthStateHash))
        {
            _logger.LogWarning("Rejected Razorpay OAuth callback for merchant {UserId}: state did not match", userId);
            throw ApiException.BadRequest("This Razorpay connection could not be completed. Please start again.");
        }

        // Single use, whatever happens next.
        connection.OauthStateHash = null;
        connection.OauthStateExpiresAt = null;

        RazorpayOauthTokens tokens;
        try
        {
            tokens = await _oauth.ExchangeCodeAsync(code, _options.Mode, ct);
        }
        catch (Exception ex) when (ex is PaymentCredentialException or PaymentProviderException)
        {
            connection.Status = MerchantConnectionStatus.Error;
            connection.StatusMessage = "Razorpay did not complete the connection.";
            connection.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            throw ApiException.BadRequest(ex.Message);
        }

        connection.Mode = MerchantConnectionMode.Oauth;
        connection.Environment = _options.Mode;
        connection.PublicKey = tokens.PublicToken;
        connection.AccessTokenCipher = _protector.Protect(tokens.AccessToken);
        connection.RefreshTokenCipher = tokens.RefreshToken is null ? null : _protector.Protect(tokens.RefreshToken);
        connection.AccessTokenExpiresAt = tokens.ExpiresAt;
        connection.ProviderAccountId = tokens.RazorpayAccountId;
        connection.KeySecretCipher = null;

        // The secret this connection's webhooks must be signed with.
        //
        // The intention is that Quotely registers the webhook at Razorpay on the merchant's
        // behalf, via the partner webhook API, so an OAuth merchant configures nothing. That call
        // is NOT made: the current documentation conflicts on the authorization scheme for it and
        // does not confirm that order.paid is an accepted event, and it cannot be exercised
        // without approved partner credentials. Implementing it on a guess would mean shipping a
        // webhook registration that silently fails.
        //
        // So until then the secret is handed to the merchant exactly as in key-pair mode and they
        // register the webhook themselves. See docs/razorpay-partner.md, "Two things still to do".
        var webhookSecret = PublicTokenGenerator.CreateToken();
        connection.WebhookSecretCipher = _protector.Protect(webhookSecret);

        MarkConnected(connection);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Merchant {UserId} connected Razorpay account {ProviderAccountId} via OAuth",
            userId, tokens.RazorpayAccountId);

        var dto = Describe(connection);
        dto.WebhookSecret = webhookSecret;
        return dto;
    }

    /// <summary>
    /// Renews an access token that is near expiry. Returns null when the renewal failed, having
    /// already recorded why — the caller then treats the connection as unusable rather than
    /// making a provider call that is certain to be refused.
    /// </summary>
    private async Task<MerchantPaymentConnection?> RefreshAsync(
        MerchantPaymentConnection connection, CancellationToken ct)
    {
        var refreshToken = Decrypt(connection.RefreshTokenCipher);

        if (string.IsNullOrWhiteSpace(refreshToken) || !_oauth.IsConfigured)
        {
            connection.Status = MerchantConnectionStatus.Expired;
            connection.StatusMessage = "The Razorpay connection has expired. Please reconnect.";
            connection.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return null;
        }

        try
        {
            var tokens = await _oauth.RefreshAsync(refreshToken, ct);

            connection.PublicKey = tokens.PublicToken;
            connection.AccessTokenCipher = _protector.Protect(tokens.AccessToken);
            // Razorpay rotates the refresh token as well; keeping the old one would leave us
            // holding a value that has already stopped working.
            if (tokens.RefreshToken is not null)
                connection.RefreshTokenCipher = _protector.Protect(tokens.RefreshToken);
            connection.AccessTokenExpiresAt = tokens.ExpiresAt;
            connection.Status = MerchantConnectionStatus.Connected;
            connection.StatusMessage = null;
            connection.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Refreshed the Razorpay access token for merchant {UserId}", connection.UserId);
            return connection;
        }
        catch (Exception ex) when (ex is PaymentCredentialException or PaymentProviderException)
        {
            _logger.LogWarning(ex, "Could not refresh the Razorpay token for merchant {UserId}", connection.UserId);

            connection.Status = MerchantConnectionStatus.Expired;
            connection.StatusMessage = "The Razorpay connection has expired. Please reconnect.";
            connection.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return null;
        }
    }

    // ---- disconnecting ---------------------------------------------------

    public async Task<MerchantConnectionDto> DisconnectAsync(Guid userId, CancellationToken ct = default)
    {
        var connection = await _db.MerchantPaymentConnections
            .FirstOrDefaultAsync(c => c.UserId == userId && c.Provider == PaymentProviders.Razorpay, ct)
            ?? throw ApiException.NotFound("Payment connection");

        // Told to Razorpay first, but never allowed to block: see IRazorpayOauthClient.RevokeAsync.
        if (connection.Mode == MerchantConnectionMode.Oauth)
        {
            var refreshToken = Decrypt(connection.RefreshTokenCipher);
            if (!string.IsNullOrWhiteSpace(refreshToken))
                await _oauth.RevokeAsync(refreshToken, "refresh_token", ct);

            var accessToken = Decrypt(connection.AccessTokenCipher);
            if (!string.IsNullOrWhiteSpace(accessToken))
                await _oauth.RevokeAsync(accessToken, "access_token", ct);
        }

        // Every credential goes, not just the status. A disconnected account that still holds a
        // merchant's key secret would be a breach waiting for a bug to expose it.
        connection.Status = MerchantConnectionStatus.Disconnected;
        connection.StatusMessage = null;
        connection.KeySecretCipher = null;
        connection.AccessTokenCipher = null;
        connection.RefreshTokenCipher = null;
        connection.WebhookSecretCipher = null;
        connection.AccessTokenExpiresAt = null;
        connection.OauthStateHash = null;
        connection.OauthStateExpiresAt = null;
        connection.PublicKey = string.Empty;
        connection.ProviderAccountId = null;
        connection.DisconnectedAt = DateTime.UtcNow;
        connection.UpdatedAt = DateTime.UtcNow;
        // A new route token on every reconnection, so a URL a merchant pasted somewhere before
        // disconnecting cannot address the connection they make afterwards.
        connection.WebhookRouteToken = PublicTokenGenerator.CreateToken();

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Merchant {UserId} disconnected their Razorpay account", userId);

        return Describe(connection);
    }

    // ---- webhooks --------------------------------------------------------

    public Task<MerchantPaymentConnection?> FindByWebhookRouteAsync(
        string routeToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(routeToken)) return Task.FromResult<MerchantPaymentConnection?>(null);

        return _db.MerchantPaymentConnections
            .FirstOrDefaultAsync(c => c.WebhookRouteToken == routeToken, ct);
    }

    public string? ReadWebhookSecret(MerchantPaymentConnection connection) =>
        Decrypt(connection.WebhookSecretCipher);

    public async Task MarkUnusableAsync(Guid connectionId, string reason, CancellationToken ct = default)
    {
        var connection = await _db.MerchantPaymentConnections
            .FirstOrDefaultAsync(c => c.Id == connectionId, ct);

        if (connection is null || connection.Status == MerchantConnectionStatus.Disconnected) return;

        connection.Status = MerchantConnectionStatus.Error;
        connection.StatusMessage = reason;
        connection.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogWarning(
            "Marked the Razorpay connection for merchant {UserId} as needing attention: {Reason}",
            connection.UserId, reason);
    }

    // ---- shared ----------------------------------------------------------

    private async Task<MerchantPaymentConnection> LoadOrCreateAsync(Guid userId, CancellationToken ct)
    {
        var connection = await _db.MerchantPaymentConnections
            .FirstOrDefaultAsync(c => c.UserId == userId && c.Provider == PaymentProviders.Razorpay, ct);

        if (connection is not null) return connection;

        connection = new MerchantPaymentConnection
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Provider = PaymentProviders.Razorpay,
            Status = MerchantConnectionStatus.Disconnected,
            WebhookRouteToken = PublicTokenGenerator.CreateToken()
        };

        _db.MerchantPaymentConnections.Add(connection);
        return connection;
    }

    private static void MarkConnected(MerchantPaymentConnection connection)
    {
        connection.Status = MerchantConnectionStatus.Connected;
        connection.StatusMessage = null;
        connection.ConnectedAt = DateTime.UtcNow;
        connection.DisconnectedAt = null;
        connection.LastVerifiedAt = DateTime.UtcNow;
        connection.UpdatedAt = DateTime.UtcNow;

        if (string.IsNullOrWhiteSpace(connection.WebhookRouteToken))
            connection.WebhookRouteToken = PublicTokenGenerator.CreateToken();
    }

    private async Task<MerchantAccountProbe> ProbeCredentialsAsync(
        MerchantPaymentContext context, CancellationToken ct)
    {
        try
        {
            return await _provider.ProbeAsync(context, ct);
        }
        catch (PaymentCredentialException ex)
        {
            throw ApiException.BadRequest(ex.Message);
        }
        catch (PaymentProviderException ex)
        {
            throw new ApiException(System.Net.HttpStatusCode.BadGateway, ex.Message);
        }
    }

    /// <summary>
    /// Refuses to store a credential we cannot encrypt. Writing a merchant's key secret in
    /// plaintext because a configuration value was missing is not a degraded mode worth having.
    /// </summary>
    private void EnsureEncryptionAvailable()
    {
        if (_protector.IsConfigured) return;

        _logger.LogError("Refusing to store merchant credentials: Encryption__Key is not configured.");
        throw new ApiException(System.Net.HttpStatusCode.ServiceUnavailable,
            "Payment connections are not available on this deployment yet.");
    }
}
