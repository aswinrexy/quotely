using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Quotely.Api.Data;

namespace Quotely.Migrations.PostgreSql;

/// <summary>
/// Lets <c>dotnet ef</c> build the PostgreSQL model without starting the API.
///
/// The connection string here is never used to connect: migrations are authored and applied
/// against whatever the running app is configured with. It exists because EF insists on one at
/// design time, and it is deliberately a placeholder rather than any real host — no credentials,
/// no Supabase project, nothing that could be mistaken for a committed secret.
/// </summary>
public class PostgreSqlDesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=quotely_design_time;Username=postgres",
                npgsql => npgsql.MigrationsAssembly(typeof(PostgreSqlDesignTimeFactory).Assembly.GetName().Name))
            .Options;

        return new AppDbContext(options);
    }
}
