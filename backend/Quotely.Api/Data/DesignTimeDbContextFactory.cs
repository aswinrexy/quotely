using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Quotely.Api.Data;

/// <summary>
/// Used by `dotnet ef`. Pick the provider with:
///   dotnet ef migrations add Init -- --provider SqlServer
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var provider = ReadArg(args, "--provider") ?? "SqlServer";
        var builder = new DbContextOptionsBuilder<AppDbContext>();

        if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
            builder.UseSqlite("Data Source=quotely.db",
                x => x.MigrationsAssembly("Quotely.Api").MigrationsHistoryTable("__EFMigrationsHistory"));
        else
            builder.UseSqlServer("Server=localhost;Database=Quotely;Trusted_Connection=False;TrustServerCertificate=True;User Id=sa;Password=placeholder");

        return new AppDbContext(builder.Options);
    }

    private static string? ReadArg(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
