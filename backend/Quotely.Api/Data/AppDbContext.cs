using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Quotely.Api.Models;

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

    /// <summary>
    /// Neither SQL Server's datetime2 nor SQLite stores a timezone, so values read back arrive as
    /// DateTimeKind.Unspecified and serialize without a "Z" — which a browser then reads as local
    /// time, shifting displayed dates. Everything we store is UTC, so say so on the way out.
    /// </summary>
    private static readonly ValueConverter<DateTime, DateTime> UtcConverter = new(
        toDatabase => toDatabase.Kind == DateTimeKind.Local ? toDatabase.ToUniversalTime() : toDatabase,
        fromDatabase => DateTime.SpecifyKind(fromDatabase, DateTimeKind.Utc));

    private static readonly ValueConverter<DateTime?, DateTime?> NullableUtcConverter = new(
        toDatabase => toDatabase.HasValue && toDatabase.Value.Kind == DateTimeKind.Local
            ? toDatabase.Value.ToUniversalTime()
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

        b.Entity<WebhookEvent>(e =>
        {
            // The idempotency key. A retried delivery fails this insert and is acknowledged
            // without being processed again.
            e.HasIndex(x => new { x.Provider, x.EventId }).IsUnique();
            e.Property(x => x.Provider).HasMaxLength(30).IsRequired();
            e.Property(x => x.EventId).HasMaxLength(120).IsRequired();
            e.Property(x => x.EventType).HasMaxLength(80).IsRequired();
            e.Property(x => x.ProviderOrderId).HasMaxLength(80);
            e.Property(x => x.ProviderPaymentId).HasMaxLength(80);
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
