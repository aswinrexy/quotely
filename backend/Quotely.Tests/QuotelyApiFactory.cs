using System.Net.Http.Headers;
using System.Net.Http.Json;
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
            // Placeholders only: the fake provider below replaces the real adapter entirely.
            ["Razorpay:KeyId"] = FakePaymentProvider.TestKeyId,
            ["Razorpay:KeySecret"] = FakePaymentProvider.TestKeySecret,
            ["Razorpay:WebhookSecret"] = FakePaymentProvider.TestWebhookSecret
        }));

        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
            services.RemoveAll(typeof(AppDbContext));
            services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));

            services.RemoveAll(typeof(IPaymentProvider));
            services.AddSingleton<IPaymentProvider>(Payments);
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

    /// <summary>Registers a fresh account and returns a client already carrying its bearer token.</summary>
    public async Task<HttpClient> CreateSignedInClientAsync(string? email = null)
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
