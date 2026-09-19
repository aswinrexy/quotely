using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Quotely.Api.Data;

namespace Quotely.Migrations.PostgreSql;

/// <summary>
/// Lets <c>dotnet ef</c> build the PostgreSQL model, and apply migrations, without starting the API.
///
/// The connection string is read from the environment — the same
/// <c>ConnectionStrings__DefaultConnection</c> variable the application itself uses — so
/// <c>dotnet ef database update</c> reaches the database the operator has configured rather than
/// a hard-coded host.
///
/// It falls back to a placeholder that cannot connect to anything. That fallback is what
/// <c>dotnet ef migrations add</c> uses: scaffolding a migration needs a connection string to
/// exist but never opens it. Applying one does open it, and will fail loudly against the
/// placeholder rather than silently touching some other database — which is the safer way round.
///
/// No credentials are committed here, and none ever should be.
/// </summary>
public class PostgreSqlDesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    /// <summary>Obviously not a real host, so a failure to connect names the cause.</summary>
    private const string Placeholder =
        "Host=localhost;Database=quotely_design_time_placeholder;Username=postgres";

    public AppDbContext CreateDbContext(string[] args)
    {
        // Double-underscore is how .NET spells configuration nesting in an environment variable;
        // the single-colon form is accepted too, because that is how the same setting is written
        // in appsettings.json and it is an easy thing to type out of habit.
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings:DefaultConnection")
            ?? Placeholder;

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsAssembly(typeof(PostgreSqlDesignTimeFactory).Assembly.GetName().Name))
            .Options;

        return new AppDbContext(options);
    }
}
