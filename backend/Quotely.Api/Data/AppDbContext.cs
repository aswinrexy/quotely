using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Quotely.Api.Models;
using Quotely.Api.Services;

namespace Quotely.Api.Data;

public class AppDbContext : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<BusinessProfile> BusinessProfiles => Set<BusinessProfile>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Quotation> Quotations => Set<Quotation>();
    public DbSet<QuotationItem> QuotationItems => Set<QuotationItem>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceItem> InvoiceItems => Set<InvoiceItem>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<WebhookEvent> WebhookEvents => Set<WebhookEvent>();
    public DbSet<MerchantPaymentConnection> MerchantPaymentConnections => Set<MerchantPaymentConnection>();

    // ---- import/export. Extra detail hanging off the existing invoice, never a second invoice.
    public DbSet<TradeInvoiceDetails> TradeInvoiceDetails => Set<TradeInvoiceDetails>();
    public DbSet<TradeLineDetails> TradeLineDetails => Set<TradeLineDetails>();
    public DbSet<TradeProfile> TradeProfiles => Set<TradeProfile>();

    // ---- Quotely's own SaaS billing. A different kind of money from everything above: these
    // ---- are businesses paying US, not customers paying them.
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<Coupon> Coupons => Set<Coupon>();
    public DbSet<CouponRedemption> CouponRedemptions => Set<CouponRedemption>();

    /// <summary>
    /// Neither SQL Server's datetime2 nor SQLite stores a timezone, so values read back arrive as
    /// DateTimeKind.Unspecified and serialize without a "Z" — which a browser then reads as local
    /// time, shifting displayed dates. Everything we store is UTC, so say so on the way out.
    ///
    /// Going in, the value is forced to a UTC kind as well. On SQL Server and SQLite the kind is
    /// merely ignored, but PostgreSQL's timestamptz rejects anything that is not Utc outright —
    /// so normalising here is what lets one model serve all three engines.
    /// </summary>
    private static readonly ValueConverter<DateTime, DateTime> UtcConverter = new(
        toDatabase => toDatabase.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(toDatabase, DateTimeKind.Utc)
            : toDatabase.ToUniversalTime(),
        fromDatabase => DateTime.SpecifyKind(fromDatabase, DateTimeKind.Utc));

    private static readonly ValueConverter<DateTime?, DateTime?> NullableUtcConverter = new(
        toDatabase => toDatabase.HasValue
            ? (toDatabase.Value.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(toDatabase.Value, DateTimeKind.Utc)
                : toDatabase.Value.ToUniversalTime())
            : toDatabase,
        fromDatabase => fromDatabase.HasValue
            ? DateTime.SpecifyKind(fromDatabase.Value, DateTimeKind.Utc)
            : fromDatabase);

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        foreach (var property in b.Model.GetEntityTypes().SelectMany(t => t.GetProperties()))
        {
            if (property.ClrType == typeof(DateTime)) property.SetValueConverter(UtcConverter);
            else if (property.ClrType == typeof(DateTime?)) property.SetValueConverter(NullableUtcConverter);
        }

        b.Entity<BusinessProfile>(e =>
        {
            e.HasIndex(x => x.UserId).IsUnique();
            e.Property(x => x.BusinessName).HasMaxLength(200).IsRequired();
            e.Property(x => x.BusinessEmail).HasMaxLength(256);
            e.Property(x => x.Phone).HasMaxLength(50);
            e.Property(x => x.AddressLine).HasMaxLength(400);
            e.Property(x => x.City).HasMaxLength(120);
            e.Property(x => x.State).HasMaxLength(120);
            e.Property(x => x.PostalCode).HasMaxLength(30);
            e.Property(x => x.Country).HasMaxLength(120);
            e.Property(x => x.TaxNumber).HasMaxLength(60);
            e.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            e.HasOne(x => x.User).WithOne(u => u.BusinessProfile)
                .HasForeignKey<BusinessProfile>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Customer>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.Name });
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.CompanyName).HasMaxLength(200);
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.Phone).HasMaxLength(50);
            e.Property(x => x.AddressLine).HasMaxLength(400);
            e.Property(x => x.City).HasMaxLength(120);
            e.Property(x => x.State).HasMaxLength(120);
            e.Property(x => x.PostalCode).HasMaxLength(30);
            e.Property(x => x.Country).HasMaxLength(120);
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.HasOne(x => x.User).WithMany(u => u.Customers)
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Product>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.Name });
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.Unit).HasMaxLength(50).IsRequired();
            e.Property(x => x.Price).HasPrecision(18, 2);
            e.Property(x => x.TaxRate).HasPrecision(5, 2);
            e.HasOne(x => x.User).WithMany(u => u.Products)
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Quotation>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.QuotationNumber }).IsUnique();
            e.HasIndex(x => new { x.UserId, x.Sequence }).IsUnique();
            e.HasIndex(x => new { x.UserId, x.Status });
            e.Property(x => x.QuotationNumber).HasMaxLength(30).IsRequired();
            // Lookup key for the public customer link. Unique so a hash collision or a
            // duplicated token can never resolve to two quotations.
            e.Property(x => x.PublicTokenHash).HasMaxLength(64);
            e.HasIndex(x => x.PublicTokenHash).IsUnique();
            e.Property(x => x.RespondedByName).HasMaxLength(150);
            e.Property(x => x.RespondedByEmail).HasMaxLength(256);
            e.Property(x => x.ResponseComment).HasMaxLength(1000);
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.Property(x => x.Terms).HasMaxLength(4000);
            e.Property(x => x.Subtotal).HasPrecision(18, 2);
            e.Property(x => x.DiscountTotal).HasPrecision(18, 2);
            e.Property(x => x.TaxTotal).HasPrecision(18, 2);
            e.Property(x => x.GrandTotal).HasPrecision(18, 2);
            e.HasOne(x => x.User).WithMany(u => u.Quotations)
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            // Restrict: a customer with quotations must not silently disappear.
            e.HasOne(x => x.Customer).WithMany(c => c.Quotations)
                .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Invoice>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.InvoiceNumber }).IsUnique();
            e.HasIndex(x => new { x.UserId, x.Sequence }).IsUnique();
            e.HasIndex(x => new { x.UserId, x.Status });
            // The import/export list filters on this constantly; the standard list never does,
            // and a composite with UserId keeps both cheap.
            e.HasIndex(x => new { x.UserId, x.Type });
            // One invoice per quotation: enforced in the database, not only in the service, so a
            // concurrent double conversion cannot slip two invoices through. QuotationId is
            // nullable since V2.4, and EF filters the index to non-null values — directly raised
            // invoices all carry NULL and so are not in competition with each other.
            e.HasIndex(x => x.QuotationId).IsUnique();
            e.Property(x => x.InvoiceNumber).HasMaxLength(30).IsRequired();
            e.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            e.Property(x => x.CustomerName).HasMaxLength(200).IsRequired();
            e.Property(x => x.CustomerCompanyName).HasMaxLength(200);
            e.Property(x => x.CustomerEmail).HasMaxLength(256);
            e.Property(x => x.CustomerPhone).HasMaxLength(50);
            e.Property(x => x.CustomerAddressLine).HasMaxLength(400);
            e.Property(x => x.CustomerCity).HasMaxLength(120);
            e.Property(x => x.CustomerState).HasMaxLength(120);
            e.Property(x => x.CustomerPostalCode).HasMaxLength(30);
            e.Property(x => x.CustomerCountry).HasMaxLength(120);
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.Property(x => x.Terms).HasMaxLength(4000);
            e.Property(x => x.Subtotal).HasPrecision(18, 2);
            e.Property(x => x.DiscountTotal).HasPrecision(18, 2);
            e.Property(x => x.TaxTotal).HasPrecision(18, 2);
            e.Property(x => x.GrandTotal).HasPrecision(18, 2);
            // Lookup key for the customer-facing payment link; unique so a token can never
            // resolve to two invoices.
            e.Property(x => x.PublicTokenHash).HasMaxLength(64);
            e.HasIndex(x => x.PublicTokenHash).IsUnique();
            e.HasOne(x => x.User).WithMany(u => u.Invoices)
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            // Restrict on both sides: an invoiced quotation and an invoiced customer must not be
            // deleted out from under a financial document.
            e.HasOne(x => x.Quotation).WithOne(q => q.Invoice)
                .HasForeignKey<Invoice>(x => x.QuotationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Customer).WithMany(c => c.Invoices)
                .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            // Cascade: the trade detail has no meaning without its invoice, and an invoice that
            // can be deleted must not be blocked by it.
            e.HasOne(x => x.TradeDetails).WithOne(t => t.Invoice)
                .HasForeignKey<TradeInvoiceDetails>(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Payment>(e =>
        {
            e.HasIndex(x => x.InvoiceId);
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            e.HasIndex(x => new { x.InvoiceId, x.Status });
            // One order may be attempted more than once, so this is an index, not a constraint.
            e.HasIndex(x => x.ProviderOrderId);
            // One provider payment maps to exactly one internal record. Enforced by the database
            // rather than by an application "if not exists" check, so a duplicate webhook and a
            // duplicate checkout callback racing each other still cannot double-count money.
            e.HasIndex(x => x.ProviderPaymentId).IsUnique();
            // At most one live payment attempt per invoice, enforced by the database. Two
            // concurrent create-order requests cannot both reserve the same balance: one insert
            // wins and the other is handed the winner's order.
            e.HasIndex(x => x.ReservationSlot).IsUnique();
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            e.Property(x => x.Provider).HasMaxLength(30).IsRequired();
            // Nullable since V2.5: a manual payment has no provider order behind it.
            e.Property(x => x.ProviderOrderId).HasMaxLength(80);
            e.Property(x => x.ProviderPaymentId).HasMaxLength(80);
            e.Property(x => x.Method).HasMaxLength(40);
            e.Property(x => x.Reference).HasMaxLength(100);
            e.Property(x => x.Notes).HasMaxLength(500);
            // Reading the ledger always asks for captured, non-voided rows on one invoice, so the
            // existing (InvoiceId, Status) index is extended to cover the void check too.
            e.HasIndex(x => new { x.InvoiceId, x.Status, x.VoidedAt });
            // Which account collected what, for reconciliation and for the admin view.
            e.HasIndex(x => x.MerchantConnectionId);
            e.Property(x => x.CustomerName).HasMaxLength(200);
            e.Property(x => x.CustomerEmail).HasMaxLength(256);
            e.Property(x => x.FailureReason).HasMaxLength(500);
            e.Property(x => x.ConcurrencyStamp).IsConcurrencyToken();
            e.HasOne(x => x.Invoice).WithMany(i => i.Payments)
                .HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
            // No second cascade path to AspNetUsers: SQL Server rejects multiple cascade routes,
            // and payments already disappear with their invoice.
            e.HasOne<AppUser>().WithMany(u => u.Payments)
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        });

        // ---- import/export -------------------------------------------------
        b.Entity<TradeInvoiceDetails>(e =>
        {
            // One detail row per invoice. In the database, not only in the service, so a retried
            // request cannot leave a document with two sets of shipping facts.
            e.HasIndex(x => x.InvoiceId).IsUnique();

            e.Property(x => x.DocumentNumber).HasMaxLength(60);
            e.Property(x => x.BuyerOrderNumber).HasMaxLength(60);
            e.Property(x => x.OtherReferences).HasMaxLength(400);

            e.Property(x => x.PartyName).HasMaxLength(200).IsRequired();
            e.Property(x => x.PartyAddress).HasMaxLength(600);
            e.Property(x => x.ConsignorName).HasMaxLength(200);
            e.Property(x => x.ConsignorAddress).HasMaxLength(600);
            e.Property(x => x.ConsigneeName).HasMaxLength(200).IsRequired();
            e.Property(x => x.ConsigneeAddress).HasMaxLength(600);
            e.Property(x => x.BuyerName).HasMaxLength(200);
            e.Property(x => x.BuyerAddress).HasMaxLength(600);
            e.Property(x => x.NotifyPartyName).HasMaxLength(200);
            e.Property(x => x.NotifyPartyAddress).HasMaxLength(600);

            e.Property(x => x.PreCarriageBy).HasMaxLength(120);
            e.Property(x => x.PlaceOfReceipt).HasMaxLength(160);
            e.Property(x => x.VesselOrFlightNumber).HasMaxLength(120);
            e.Property(x => x.PortOfLoading).HasMaxLength(160);
            e.Property(x => x.PortOfDischarge).HasMaxLength(160);
            e.Property(x => x.FinalDestination).HasMaxLength(160);
            e.Property(x => x.CountryOfOrigin).HasMaxLength(120);
            e.Property(x => x.CountryOfFinalDestination).HasMaxLength(120);

            e.Property(x => x.TermsOfDelivery).HasMaxLength(300);
            e.Property(x => x.TermsOfPayment).HasMaxLength(300);
            e.Property(x => x.PricingTerm).HasMaxLength(40);

            e.Property(x => x.IecNumber).HasMaxLength(40);
            e.Property(x => x.GstNumber).HasMaxLength(40);
            e.Property(x => x.PanNumber).HasMaxLength(40);
            e.Property(x => x.ApedaRegistrationNumber).HasMaxLength(60);

            // Long enough for several lines of declaration without inviting an essay.
            e.Property(x => x.HeaderDeclarations).HasMaxLength(2000);
            e.Property(x => x.FooterDeclaration).HasMaxLength(2000);
            e.Property(x => x.AuthorisedSignatory).HasMaxLength(200);

            // Weights to three places: agricultural consignments are quoted in fractional kilos.
            e.Property(x => x.TotalNetWeight).HasPrecision(18, 3);
            e.Property(x => x.TotalGrossWeight).HasPrecision(18, 3);
            e.Property(x => x.TotalPackages).HasPrecision(18, 3);
            e.Property(x => x.WeightUnit).HasMaxLength(20).IsRequired();
        });

        b.Entity<TradeLineDetails>(e =>
        {
            e.HasIndex(x => x.InvoiceItemId).IsUnique();
            e.Property(x => x.MarksAndNumbers).HasMaxLength(120);
            e.Property(x => x.Dimension).HasMaxLength(120);
            // Text, never numeric: HS codes are fixed-width and carry leading zeros.
            e.Property(x => x.HsCode).HasMaxLength(30);
            e.Property(x => x.NetWeight).HasPrecision(18, 3);
            e.Property(x => x.GrossWeight).HasPrecision(18, 3);
            e.Property(x => x.QuantityUnit).HasMaxLength(40);
            e.Property(x => x.RateLabel).HasMaxLength(80);
        });

        b.Entity<TradeProfile>(e =>
        {
            e.HasIndex(x => x.UserId).IsUnique();
            e.Property(x => x.IecNumber).HasMaxLength(40);
            e.Property(x => x.GstNumber).HasMaxLength(40);
            e.Property(x => x.PanNumber).HasMaxLength(40);
            e.Property(x => x.ApedaRegistrationNumber).HasMaxLength(60);
            e.Property(x => x.PartyNameOverride).HasMaxLength(200);
            e.Property(x => x.PartyAddressOverride).HasMaxLength(600);
            e.Property(x => x.DefaultCountryOfOrigin).HasMaxLength(120);
            e.Property(x => x.DefaultTermsOfDelivery).HasMaxLength(300);
            e.Property(x => x.DefaultTermsOfPayment).HasMaxLength(300);
            e.Property(x => x.DefaultPricingTerm).HasMaxLength(40);
            e.Property(x => x.DefaultPortOfLoading).HasMaxLength(160);
            e.Property(x => x.DefaultPreCarriageBy).HasMaxLength(120);
            e.Property(x => x.DefaultHeaderDeclarations).HasMaxLength(2000);
            e.Property(x => x.DefaultFooterDeclaration).HasMaxLength(2000);
            e.Property(x => x.DefaultAuthorisedSignatory).HasMaxLength(200);
            e.Property(x => x.DefaultCurrency).HasMaxLength(3);
            e.HasOne(x => x.User).WithMany()
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<MerchantPaymentConnection>(e =>
        {
            // One payment account per business per provider. Enforced by the database so two
            // simultaneous "connect" requests cannot leave a tenant with two connections, of
            // which one would silently never be used.
            e.HasIndex(x => new { x.UserId, x.Provider }).IsUnique();

            // How a webhook delivery finds its connection. Unique because it is an identifier,
            // and indexed because every delivery is a lookup on it.
            e.HasIndex(x => x.WebhookRouteToken).IsUnique();

            // Answering "which businesses can take payments?" for the admin view without reading
            // every row and decrypting nothing.
            e.HasIndex(x => new { x.Provider, x.Status });

            e.Property(x => x.Provider).HasMaxLength(30).IsRequired();
            e.Property(x => x.ProviderAccountId).HasMaxLength(60);
            e.Property(x => x.PublicKey).HasMaxLength(120);
            e.Property(x => x.DisplayName).HasMaxLength(100);
            e.Property(x => x.StatusMessage).HasMaxLength(300);
            e.Property(x => x.WebhookRouteToken).HasMaxLength(64).IsRequired();
            e.Property(x => x.OauthStateHash).HasMaxLength(PublicTokenGenerator.HashLength);

            // Ciphertext is longer than the plaintext it protects — a nonce, a tag and base64
            // expansion on top of the secret — so these are sized generously rather than to the
            // length of a Razorpay key.
            e.Property(x => x.KeySecretCipher).HasMaxLength(1024);
            e.Property(x => x.AccessTokenCipher).HasMaxLength(4096);
            e.Property(x => x.RefreshTokenCipher).HasMaxLength(4096);
            e.Property(x => x.WebhookSecretCipher).HasMaxLength(1024);

            e.Property(x => x.ConcurrencyStamp).IsConcurrencyToken();

            e.HasOne(x => x.User).WithMany()
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Subscription>(e =>
        {
            // One subscription per business. Enforced by the database so two requests from a new
            // account cannot each create a trial.
            e.HasIndex(x => x.UserId).IsUnique();

            // How a billing webhook finds the subscription it is about.
            e.HasIndex(x => x.ProviderSubscriptionId).IsUnique();

            // "Whose access is about to lapse?" — the query a reconciliation pass would run.
            e.HasIndex(x => new { x.Status, x.TrialEnd });

            e.Property(x => x.PlanCode).HasMaxLength(40).IsRequired();
            e.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            e.Property(x => x.Provider).HasMaxLength(30).IsRequired();
            e.Property(x => x.ProviderSubscriptionId).HasMaxLength(80);
            e.Property(x => x.ProviderPlanId).HasMaxLength(80);
            e.Property(x => x.StatusMessage).HasMaxLength(300);
            e.Property(x => x.Price).HasPrecision(18, 2);
            e.Property(x => x.ConcurrencyStamp).IsConcurrencyToken();

            e.HasOne(x => x.User).WithOne()
                .HasForeignKey<Subscription>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Coupon>(e =>
        {
            // The lookup key, and the guarantee that one code means one coupon.
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(40).IsRequired();
            e.Property(x => x.Description).HasMaxLength(200).IsRequired();
            e.Property(x => x.ConcurrencyStamp).IsConcurrencyToken();
        });

        b.Entity<CouponRedemption>(e =>
        {
            // ONE REDEMPTION PER ACCOUNT, enforced here rather than by a service-level check.
            // Two concurrent requests can both pass "have they already redeemed this?"; only one
            // of them can win this insert.
            e.HasIndex(x => new { x.CouponId, x.UserId }).IsUnique();
            e.HasIndex(x => x.UserId);
            e.Property(x => x.CodeUsed).HasMaxLength(40).IsRequired();

            e.HasOne(x => x.Coupon).WithMany(c => c.Redemptions)
                .HasForeignKey(x => x.CouponId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Subscription).WithMany(s => s.Redemptions)
                .HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.Cascade);
            // No second cascade path to AspNetUsers: SQL Server rejects multiple cascade routes,
            // and a redemption already disappears with its subscription.
            e.HasOne(x => x.User).WithMany()
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<WebhookEvent>(e =>
        {
            // The idempotency key. A retried delivery fails this insert and is acknowledged
            // without being processed again.
            e.HasIndex(x => new { x.Provider, x.EventId }).IsUnique();
            e.Property(x => x.Provider).HasMaxLength(30).IsRequired();
            e.Property(x => x.EventType).HasMaxLength(80).IsRequired();
            e.Property(x => x.ProviderOrderId).HasMaxLength(80);
            e.Property(x => x.ProviderPaymentId).HasMaxLength(80);
            // The connection-scoped event id is longer than a bare provider one, so the column
            // grew with it. 120 would now truncate a legitimate key and turn two distinct events
            // into an accidental duplicate.
            e.Property(x => x.EventId).HasMaxLength(180).IsRequired();
            e.HasIndex(x => new { x.UserId, x.ReceivedAt });
        });

        b.Entity<InvoiceItem>(e =>
        {
            e.HasIndex(x => x.InvoiceId);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.Unit).HasMaxLength(50).IsRequired();
            e.Property(x => x.Quantity).HasPrecision(18, 3);
            e.Property(x => x.UnitPrice).HasPrecision(18, 2);
            e.Property(x => x.Discount).HasPrecision(18, 2);
            e.Property(x => x.TaxRate).HasPrecision(5, 2);
            e.Property(x => x.LineSubtotal).HasPrecision(18, 2);
            e.Property(x => x.LineTax).HasPrecision(18, 2);
            e.Property(x => x.LineTotal).HasPrecision(18, 2);
            e.HasOne(x => x.Invoice).WithMany(i => i.Items)
                .HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.TradeDetails).WithOne(t => t.InvoiceItem)
                .HasForeignKey<TradeLineDetails>(x => x.InvoiceItemId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<QuotationItem>(e =>
        {
            e.HasIndex(x => x.QuotationId);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.Unit).HasMaxLength(50).IsRequired();
            e.Property(x => x.Quantity).HasPrecision(18, 3);
            e.Property(x => x.UnitPrice).HasPrecision(18, 2);
            e.Property(x => x.Discount).HasPrecision(18, 2);
            e.Property(x => x.TaxRate).HasPrecision(5, 2);
            e.Property(x => x.LineSubtotal).HasPrecision(18, 2);
            e.Property(x => x.LineTax).HasPrecision(18, 2);
            e.Property(x => x.LineTotal).HasPrecision(18, 2);
            e.HasOne(x => x.Quotation).WithMany(q => q.Items)
                .HasForeignKey(x => x.QuotationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Product).WithMany()
                .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.SetNull);
        });
    }

    public override int SaveChanges()
    {
        Touch();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        Touch();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void Touch()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified)) continue;
            if (entry.Metadata.FindProperty("UpdatedAt") is not null)
                entry.Property("UpdatedAt").CurrentValue = now;
            if (entry.State == EntityState.Added && entry.Metadata.FindProperty("CreatedAt") is not null)
                entry.Property("CreatedAt").CurrentValue = now;
        }
    }
}
