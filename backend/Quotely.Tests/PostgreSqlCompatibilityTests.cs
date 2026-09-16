using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Quotely.Api.Data;
using Quotely.Api.Models;
using Xunit;

namespace Quotely.Tests;

/// <summary>
/// The model has to mean the same thing on PostgreSQL as it does on SQL Server, because free-tier
/// hosting puts Quotely on Postgres while SQL Server remains the supported target.
///
/// These assertions are about semantics, not spelling. Money must stay exact decimal rather than
/// drifting to a float; a date must stay a date; and the unique indexes that carry real
/// invariants — one live payment attempt per invoice, one invoice per quotation, one quotation per
/// token — must keep behaving as "unique among the rows that have a value". SQL Server needs a
/// filtered index to get that; PostgreSQL gets it for free because it treats NULLs as distinct.
/// Either way the guarantee is identical, and this is where that claim is checked.
///
/// No database is contacted: EF builds the provider's model from the same OnModelCreating that the
/// running application uses, which is exactly what would go wrong first if the two diverged.
/// </summary>
public class PostgreSqlCompatibilityTests
{
    private static AppDbContext PostgresContext() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=quotely_model_only;Username=postgres")
            .Options);

    private static string? ColumnType<TEntity>(AppDbContext db, string property) where TEntity : class =>
        db.Model.FindEntityType(typeof(TEntity))!.FindProperty(property)!.GetColumnType();

    [Theory]
    [InlineData(nameof(Invoice.Subtotal))]
    [InlineData(nameof(Invoice.DiscountTotal))]
    [InlineData(nameof(Invoice.TaxTotal))]
    [InlineData(nameof(Invoice.GrandTotal))]
    public void Invoice_money_is_exact_decimal(string property)
    {
        using var db = PostgresContext();

        ColumnType<Invoice>(db, property).Should().Be("numeric(18,2)");
    }

    [Fact]
    public void Payment_amounts_are_exact_decimal()
    {
        using var db = PostgresContext();

        ColumnType<Payment>(db, nameof(Payment.Amount)).Should().Be("numeric(18,2)");
    }

    [Fact]
    public void Line_quantities_and_tax_rates_keep_their_precision()
    {
        using var db = PostgresContext();

        ColumnType<InvoiceItem>(db, nameof(InvoiceItem.Quantity)).Should().Be("numeric(18,3)");
        ColumnType<InvoiceItem>(db, nameof(InvoiceItem.TaxRate)).Should().Be("numeric(5,2)");
        ColumnType<QuotationItem>(db, nameof(QuotationItem.Quantity)).Should().Be("numeric(18,3)");
        ColumnType<QuotationItem>(db, nameof(QuotationItem.TaxRate)).Should().Be("numeric(5,2)");
    }

    [Fact]
    public void Calendar_dates_stay_dates_and_instants_carry_a_timezone()
    {
        using var db = PostgresContext();

        ColumnType<Invoice>(db, nameof(Invoice.DueDate)).Should().Be("date");
        ColumnType<Quotation>(db, nameof(Quotation.ValidUntil)).Should().Be("date");
        // Everything stored is UTC, and timestamptz is the type that says so.
        ColumnType<Payment>(db, nameof(Payment.PaidAt)).Should().Be("timestamp with time zone");
        ColumnType<Payment>(db, nameof(Payment.VoidedAt)).Should().Be("timestamp with time zone");
    }

    [Fact]
    public void Identifiers_are_native_uuids_rather_than_text()
    {
        using var db = PostgresContext();

        ColumnType<Invoice>(db, nameof(Invoice.Id)).Should().Be("uuid");
        ColumnType<Payment>(db, nameof(Payment.ReservationSlot)).Should().Be("uuid");
    }

    /// <summary>
    /// The four uniqueness rules that are load-bearing. On PostgreSQL they need no index filter:
    /// rows with NULL are already distinct, so a partially populated column is constrained exactly
    /// where it has a value and left alone where it does not.
    /// </summary>
    [Theory]
    [InlineData(typeof(Quotation), nameof(Quotation.PublicTokenHash))]
    [InlineData(typeof(Invoice), nameof(Invoice.PublicTokenHash))]
    [InlineData(typeof(Invoice), nameof(Invoice.QuotationId))]
    [InlineData(typeof(Payment), nameof(Payment.ProviderPaymentId))]
    [InlineData(typeof(Payment), nameof(Payment.ReservationSlot))]
    public void Partial_uniqueness_survives_the_move_to_postgres(Type entity, string property)
    {
        using var db = PostgresContext();

        var index = db.Model.FindEntityType(entity)!.GetIndexes()
            .Single(i => i.Properties.Count == 1 && i.Properties[0].Name == property);

        index.IsUnique.Should().BeTrue();
        db.Model.FindEntityType(entity)!.FindProperty(property)!.IsNullable
            .Should().BeTrue("the constraint only has to hold where a value exists");
        index.GetFilter().Should().BeNull(
            "PostgreSQL treats NULLs as distinct, so the filter SQL Server needs would be redundant");
    }

    [Fact]
    public void The_whole_model_builds_for_postgres()
    {
        using var db = PostgresContext();

        // Relational model construction is where a provider-incompatible mapping surfaces.
        db.Model.GetRelationalModel().Tables.Should().NotBeEmpty();
    }
}
