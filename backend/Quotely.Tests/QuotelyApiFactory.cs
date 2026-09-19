using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quotely.Api.Data;
using Quotely.Api.DTOs;
using Quotely.Api.Payments;

namespace Quotely.Tests;

/// <summary>
/// Boots the real API against a private in-memory SQLite database, so requests exercise the
/// actual middleware, authentication and EF pipeline.
/// </summary>
public class QuotelyApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    /// <summary>
    /// The stand-in payment provider. Tests arrange outcomes on it; nothing in the suite ever
    /// reaches Razorpay over the network.
    /// </summary>
    public FakePaymentProvider Payments { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);

        builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite",
            ["Database:AutoMigrate"] = "false",
            ["Seed:Enabled"] = "false",
            ["Jwt:Key"] = "integration-test-signing-key-at-least-32-chars",
            ["ConnectionStrings:DefaultConnection"] = "DataSource=:memory:",
            // Quotely's OWN billing account. Placeholders only: the fake provider below
            // replaces the real adapter entirely, and merchant payments do not read these at all.
            ["Razorpay:Mode"] = "Test",
            ["Razorpay:KeyId"] = "rzp_test_quotely_platform",
            ["Razorpay:KeySecret"] = "platform_key_secret_for_tests",
            ["Razorpay:WebhookSecret"] = "platform_webhook_secret_for_tests",
            // A real 32-byte key, so merchant credentials are genuinely encrypted in the suite
            // rather than the encryption being stubbed out. Test-only, and not a secret: it
            // protects nothing but throwaway fake keys in an in-memory database.
            ["Encryption:Key"] = "dGVzdC1vbmx5LWtleS1kby1ub3QtdXNlLWluLXByb2Q="
        }));

        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
            services.RemoveAll(typeof(AppDbContext));
            services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));

            services.RemoveAll(typeof(IMerchantPaymentProvider));
            services.AddSingleton<IMerchantPaymentProvider>(Payments);
        });
    }

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    public new async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
        await base.DisposeAsync();
    }

    /// <summary>
    /// Reads straight from the database so tests can assert on columns the API never exposes,
    /// such as the stored public token hash.
    /// </summary>
    public async Task<T> ReadQuotationAsync<T>(Guid quotationId, Func<Quotely.Api.Models.Quotation, T> select)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var quotation = await db.Quotations.AsNoTracking().FirstAsync(q => q.Id == quotationId);
        return select(quotation);
    }

    /// <summary>Reads payment rows directly, so tests can assert on columns the API never exposes.</summary>
    public async Task<List<Quotely.Api.Models.Payment>> ReadPaymentsAsync(Guid invoiceId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Payments.AsNoTracking().Where(p => p.InvoiceId == invoiceId).ToListAsync();
    }

    /// <summary>Reads the stored token hash, to prove the raw token is never persisted.</summary>
    public async Task<string?> ReadInvoiceTokenHashAsync(Guid invoiceId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Invoices.AsNoTracking().Where(i => i.Id == invoiceId)
            .Select(i => i.PublicTokenHash).FirstAsync();
    }

    /// <summary>Counts the invoices raised against one quotation, straight from the database.</summary>
    public async Task<int> CountInvoicesAsync(Guid quotationId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Invoices.AsNoTracking().CountAsync(i => i.QuotationId == quotationId);
    }

    /// <summary>
    /// A business's own payment connection, as the test needs to speak to it: where its webhooks
    /// are delivered, and the secret they must be signed with. Both are per business — that is
    /// the whole point — so a test that wants to impersonate one business at another's endpoint
    /// has to mix these up deliberately rather than by accident.
    /// </summary>
    public sealed record MerchantTestConnection(string WebhookUrl, string WebhookSecret, string KeySecret);

    /// <summary>
    /// Matches what the API actually emits. The application serialises enums as names, so a
    /// reader using the default numeric convention would fail on every response carrying one —
    /// and a test that cannot read a status is not a test of anything.
    /// </summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ConcurrentDictionary<HttpClient, MerchantTestConnection> _connections = new();

    /// <summary>The connection made for a signed-in client by <see cref="ConnectRazorpayAsync"/>.</summary>
    public MerchantTestConnection ConnectionFor(HttpClient client) =>
        _connections.TryGetValue(client, out var connection)
            ? connection
            : throw new InvalidOperationException("This client has no Razorpay connection.");

    /// <summary>
    /// Connects a Razorpay account for the signed-in business, capturing the webhook secret the
    /// server generated — the one and only time that value is available, exactly as a real owner
    /// would see it.
    ///
    /// Almost every payment test needs this now: an invoice belonging to a business with no
    /// payment connection is deliberately not payable, which is the point of the milestone.
    /// </summary>
    public async Task<MerchantTestConnection> ConnectRazorpayAsync(
        HttpClient client,
        string keyId = FakePaymentProvider.TestKeyId,
        string keySecret = FakePaymentProvider.TestKeySecret)
    {
        var response = await client.PostAsJsonAsync("/api/settings/payments/razorpay/keys", new
        {
            keyId,
            keySecret
        });

        response.EnsureSuccessStatusCode();
        var dto = (await response.Content.ReadFromJsonAsync<MerchantConnectionDto>(Json))!;

        var connection = new MerchantTestConnection(
            dto.WebhookUrl ?? throw new InvalidOperationException("Connecting returned no webhook URL."),
            dto.WebhookSecret ?? throw new InvalidOperationException("Connecting returned no webhook secret."),
            keySecret);

        _connections[client] = connection;
        return connection;
    }

    /// <summary>
    /// Registers a fresh account and returns a client already carrying its bearer token.
    ///
    /// A payment connection is made by default, because that is now the precondition for an
    /// invoice being payable at all. Tests about a business that has NOT connected one pass
    /// <paramref name="connectPayments"/> as false and say so.
    /// </summary>
    public async Task<HttpClient> CreateSignedInClientAsync(string? email = null, bool connectPayments = true)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = email ?? $"user-{Guid.NewGuid():N}@example.com",
            password = "Test@12345",
            fullName = "Test Owner",
            businessName = "Test Business"
        });

        response.EnsureSuccessStatusCode();
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);

        if (connectPayments) await ConnectRazorpayAsync(client);

        return client;
    }
}

file static class ServiceCollectionExtensions
{
    public static void RemoveAll(this IServiceCollection services, Type serviceType)
    {
        foreach (var descriptor in services.Where(d => d.ServiceType == serviceType).ToList())
            services.Remove(descriptor);
    }
}
