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
using Quotely.Api.Billing;
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
    /// <summary>
    /// A private SQLite database in a temporary file, not <c>:memory:</c>.
    ///
    /// An in-memory database lives inside one connection, so every request in the suite shared a
    /// single one — and a single connection cannot have two transactions open at once. That made
    /// genuine concurrency untestable: four simultaneous requests failed with "cannot start a
    /// transaction within a transaction" rather than racing the way they would in production.
    ///
    /// A file lets each request open its own connection, exactly as a deployment does, so the
    /// concurrency tests exercise real database locking and real unique-index collisions. The
    /// file is private to one test class and deleted afterwards.
    /// </summary>
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"quotely-tests-{Guid.NewGuid():N}.db");

    private string ConnectionString =>
        new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Pooling = true,
            // Seconds to wait for a writer's lock before giving up. Concurrent writers are the
            // point of several tests; without this they would fail as "database is locked"
            // instead of queueing the way a real one does.
            DefaultTimeout = 30
        }.ToString();

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
            ["ConnectionStrings:DefaultConnection"] = ConnectionString,
            // Quotely's OWN billing account. Placeholders only: the fake provider below
            // replaces the real adapter entirely, and merchant payments do not read these at all.
            ["Razorpay:Mode"] = "Test",
            ["Razorpay:KeyId"] = "rzp_test_quotely_platform",
            ["Razorpay:KeySecret"] = "platform_key_secret_for_tests",
            ["Razorpay:WebhookSecret"] = PlatformWebhookSecret,
            // A real 32-byte key, so merchant credentials are genuinely encrypted in the suite
            // rather than the encryption being stubbed out. Test-only, and not a secret: it
            // protects nothing but throwaway fake keys in an in-memory database.
            ["Encryption:Key"] = "dGVzdC1vbmx5LWtleS1kby1ub3QtdXNlLWluLXByb2Q=",
            ["Admin:Emails:0"] = AdminEmail
        }));

        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
            services.RemoveAll(typeof(AppDbContext));
            services.AddDbContext<AppDbContext>(options => options.UseSqlite(ConnectionString));

            services.RemoveAll(typeof(IMerchantPaymentProvider));
            services.AddSingleton<IMerchantPaymentProvider>(Payments);
        });
    }

    public async Task InitializeAsync()
    {
        using (var connection = new SqliteConnection(ConnectionString))
        {
            await connection.OpenAsync();
            // Write-ahead logging, so a reader is not blocked by the writer. Without it the
            // concurrency tests spend their time queueing behind each other rather than racing.
            await using var pragma = connection.CreateCommand();
            pragma.CommandText = "PRAGMA journal_mode=WAL;";
            await pragma.ExecuteNonQueryAsync();
        }

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();

        // The application seeds its coupons on start-up, which for this factory happens before
        // the schema above exists. Seeding here instead gives the suite the same coupons a real
        // deployment has, rather than a database where QUOTELY6 does not exist.
        await CouponSeeder.SeedAsync(Services);
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();

        // Pooled connections keep the file open, so they have to be released before it can go.
        SqliteConnection.ClearAllPools();

        foreach (var path in new[] { _databasePath, _databasePath + "-wal", _databasePath + "-shm" })
        {
            // A leftover temp file is not worth failing a test run over.
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
        }
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
    /// <summary>
    /// QUOTELY'S OWN webhook secret — the one that verifies subscription billing events. Kept
    /// distinct from every merchant secret in the suite, because proving the two cannot be
    /// substituted for each other is one of the things the tests are for.
    /// </summary>
    public const string PlatformWebhookSecret = "quotely_platform_webhook_secret_for_tests";

    /// <summary>The one account the suite treats as an administrator.</summary>
    public const string AdminEmail = "founder@quotely.test";

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
    /// <param name="seedCatalogue">
    /// Gives the new business one catalogue entry, because an invoice or a quotation cannot be
    /// created without one — see <c>CatalogueGuard</c>. Every real business will have a catalogue
    /// before it bills anyone, so the default here matches reality rather than making each of the
    /// thirty-odd document tests restate the same setup.
    ///
    /// Pass false when the test is ABOUT the catalogue itself: anything counting products against
    /// a free-tier limit has to start from zero, or the seed silently spends one of the allowance.
    /// </param>
    public async Task<HttpClient> CreateSignedInClientAsync(
        string? email = null, bool connectPayments = true, bool seedCatalogue = true)
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

        if (seedCatalogue) await SeedCatalogueEntryAsync(auth.User.Id);
        if (connectPayments) await ConnectRazorpayAsync(client);

        return client;
    }

    /// <summary>
    /// Inserted straight into the database rather than posted to /api/products, so that a factory
    /// running with free-tier enforcement on cannot have its own setup refused by the very limits
    /// the test is there to exercise.
    /// </summary>
    private async Task SeedCatalogueEntryAsync(Guid userId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Products.Add(new Quotely.Api.Models.Product
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = "Seeded catalogue entry",
            Unit = "Service",
            Price = 1000m,
            TaxRate = 18m
        });
        await db.SaveChangesAsync();
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
