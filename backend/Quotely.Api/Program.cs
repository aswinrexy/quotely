using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using QuestPDF.Infrastructure;
using Quotely.Api.Data;
using Quotely.Api.Middleware;
using Quotely.Api.Models;
using Quotely.Api.Payments;
using Quotely.Api.Pdf;
using Quotely.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// ---- configuration -------------------------------------------------------
// Secrets come from configuration/environment (JWT__KEY, ConnectionStrings__DefaultConnection),
// never from source control.
var jwtSection = builder.Configuration.GetSection(JwtOptions.SectionName);
builder.Services.Configure<JwtOptions>(jwtSection);
var jwtOptions = jwtSection.Get<JwtOptions>() ?? new JwtOptions();

if (string.IsNullOrWhiteSpace(jwtOptions.Key) || jwtOptions.Key.Length < 32)
{
    if (builder.Environment.IsDevelopment())
    {
        jwtOptions.Key = "dev-only-signing-key-change-me-please-32+chars";
        builder.Services.PostConfigure<JwtOptions>(o => o.Key = jwtOptions.Key);
    }
    else
    {
        throw new InvalidOperationException(
            "Jwt:Key must be configured with at least 32 characters (set the JWT__KEY environment variable).");
    }
}

// Render (and every other PaaS) hands the port to listen on in PORT and terminates TLS itself,
// so the app must bind every interface rather than loopback. Locally PORT is unset and Kestrel's
// own configuration continues to apply untouched.
var port = builder.Configuration["PORT"];
if (!string.IsNullOrWhiteSpace(port))
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// Hosted logs are collected as text, so structured JSON lines are what make them searchable.
// Nothing secret is ever logged: connection strings and keys are read into options objects and
// never written out.
if (!builder.Environment.IsDevelopment())
{
    builder.Logging.ClearProviders();
    builder.Logging.AddJsonConsole(o => o.IncludeScopes = true);
}

var provider = builder.Configuration["Database:Provider"] ?? "SqlServer";
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
                       ?? "Data Source=quotely.db";

builder.Services.AddDbContext<AppDbContext>(options =>
{
    if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
    {
        options.UseSqlite(connectionString);
    }
    else if (provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase)
             || provider.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase))
    {
        // PostgreSQL migrations live in their own assembly: the same model produces different DDL
        // on each engine, and one migration history cannot describe both. The SQL Server set in
        // this project is untouched by it.
        options.UseNpgsql(connectionString, npgsql =>
        {
            npgsql.EnableRetryOnFailure();
            npgsql.MigrationsAssembly("Quotely.Migrations.PostgreSql");
        });
    }
    else
    {
        options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure());
    }
});

// ---- identity + auth -----------------------------------------------------
builder.Services
    .AddIdentityCore<AppUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = false;
        options.Lockout.MaxFailedAccessAttempts = 10;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key)),
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorization();

// ---- app services --------------------------------------------------------
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IQuotationService, QuotationService>();
builder.Services.AddScoped<IPublicQuotationService, PublicQuotationService>();
builder.Services.AddScoped<IInvoiceService, InvoiceService>();
builder.Services.AddScoped<IPublicInvoiceService, PublicInvoiceService>();
builder.Services.AddScoped<ICustomerSummaryService, CustomerSummaryService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<IWebhookService, WebhookService>();

// ---- payments ----
// Credentials come from configuration only (Razorpay__KeyId, Razorpay__KeySecret,
// Razorpay__WebhookSecret). Nothing is hard-coded and the secrets never leave the server.
builder.Services.Configure<RazorpayOptions>(builder.Configuration.GetSection(RazorpayOptions.SectionName));
builder.Services.AddHttpClient<IPaymentProvider, RazorpayPaymentProvider>();
builder.Services.Configure<PublicLinkOptions>(builder.Configuration.GetSection(PublicLinkOptions.SectionName));
builder.Services.AddSingleton<IPdfService, PdfService>();

builder.Services
    .AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Model-binding/DataAnnotation failures use the same envelope as everything else.
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var message = context.ModelState
            .SelectMany(kvp => kvp.Value!.Errors.Select(e => e.ErrorMessage))
            .FirstOrDefault(m => !string.IsNullOrWhiteSpace(m)) ?? "The submitted values are not valid.";
        return new BadRequestObjectResult(new { message, status = 400 });
    };
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                  ?? new[] { "http://localhost:3000" };

builder.Services.AddCors(options => options.AddPolicy("web", policy => policy
    .WithOrigins(corsOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .WithExposedHeaders("Content-Disposition")));

QuestPDF.Settings.License = LicenseType.Community;

var app = builder.Build();

PdfFonts.Register(app.Environment.ContentRootPath, app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("PdfFonts"));

// Behind Render's proxy the request arrives over plain HTTP; without this the app would think
// every request was insecure and would report the proxy's address as the client's.
var forwarded = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
// The platform's load balancer is not on a loopback address, and its address is not known ahead
// of time, so the default proxy allow-list would discard the headers. This is safe only because
// nothing but that platform can reach the container: the app is never exposed directly.
forwarded.KnownIPNetworks.Clear();
forwarded.KnownProxies.Clear();
app.UseForwardedHeaders(forwarded);

app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    app.UseHsts();
}

app.UseCors("web");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

// ---- database ------------------------------------------------------------
if (builder.Configuration.GetValue("Database:AutoMigrate", app.Environment.IsDevelopment()))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    // SQL Server and PostgreSQL each have a migration history to apply. SQLite is the no-Docker
    // local fallback and has no migrations of its own, so its schema is created from the model.
    if (db.Database.IsSqlite())
        await db.Database.EnsureCreatedAsync();
    else
        await db.Database.MigrateAsync();
}

if (builder.Configuration.GetValue("Seed:Enabled", app.Environment.IsDevelopment()))
    await DevSeeder.SeedAsync(app.Services);

app.Run();

/// <summary>Exposed so the integration tests can boot the API with WebApplicationFactory.</summary>
public partial class Program { }
