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
            // concurrent double conversion cannot slip two invoices through.
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
            e.HasOne(x => x.User).WithMany(u => u.Invoices)
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            // Restrict on both sides: an invoiced quotation and an invoiced customer must not be
            // deleted out from under a financial document.
            e.HasOne(x => x.Quotation).WithOne(q => q.Invoice)
                .HasForeignKey<Invoice>(x => x.QuotationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Customer).WithMany(c => c.Invoices)
                .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
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
