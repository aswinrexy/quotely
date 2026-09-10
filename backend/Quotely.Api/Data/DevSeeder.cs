using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Quotely.Api.Models;
using Quotely.Api.Services;

namespace Quotely.Api.Data;

/// <summary>
/// Development-only sample data: one demo business with customers, services and a quotation,
/// so a fresh clone has something to click through. Enabled via Seed:Enabled (default: Development).
/// </summary>
public static class DevSeeder
{
    public const string DemoEmail = "demo@quotely.app";
    public const string DemoPassword = "Demo@12345";

    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DevSeeder");

        if (await users.FindByEmailAsync(DemoEmail) is not null)
        {
            logger.LogInformation("Demo data already present; skipping seed.");
            return;
        }

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = DemoEmail,
            Email = DemoEmail,
            FullName = "Demo Owner",
            EmailConfirmed = true
        };

        var created = await users.CreateAsync(user, DemoPassword);
        if (!created.Succeeded)
        {
            logger.LogWarning("Could not create demo user: {Errors}",
                string.Join("; ", created.Errors.Select(e => e.Description)));
            return;
        }

        db.BusinessProfiles.Add(new BusinessProfile
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            BusinessName = "ABC Electricals",
            BusinessEmail = "hello@abcelectricals.example",
            Phone = "+91 98765 43210",
            AddressLine = "123 Main Street, Anna Nagar",
            City = "Chennai",
            State = "Tamil Nadu",
            PostalCode = "600040",
            Country = "India",
            TaxNumber = "33ABCDE1234F1Z5",
            Currency = "INR"
        });

        var customers = new[]
        {
            new Customer
            {
                Id = Guid.NewGuid(), UserId = user.Id,
                Name = "John Smith", CompanyName = "John Smith Construction",
                Email = "john@smithconstruction.example", Phone = "+91 91234 56780",
                AddressLine = "42 Beach Road", City = "Chennai", State = "Tamil Nadu",
                PostalCode = "600006", Country = "India",
                Notes = "Prefers weekday site visits."
            },
            new Customer
            {
                Id = Guid.NewGuid(), UserId = user.Id,
                Name = "Priya Raman", CompanyName = "Raman Interiors",
                Email = "priya@ramaninteriors.example", Phone = "+91 90000 11223",
                AddressLine = "8 Nungambakkam High Road", City = "Chennai", State = "Tamil Nadu",
                PostalCode = "600034", Country = "India"
            }
        };
        db.Customers.AddRange(customers);

        var products = new[]
        {
            new Product { Id = Guid.NewGuid(), UserId = user.Id, Name = "AC Installation", Description = "Split AC installation including mounting and gas charging.", Unit = "Service", Price = 5000m, TaxRate = 18m },
            new Product { Id = Guid.NewGuid(), UserId = user.Id, Name = "AC Maintenance", Description = "Deep cleaning and performance check.", Unit = "Service", Price = 1500m, TaxRate = 18m },
            new Product { Id = Guid.NewGuid(), UserId = user.Id, Name = "Electrical Wiring", Description = "Concealed wiring work, per hour.", Unit = "Hour", Price = 800m, TaxRate = 18m },
            new Product { Id = Guid.NewGuid(), UserId = user.Id, Name = "Distribution Board", Description = "8-way DB with MCBs, supply and fit.", Unit = "Piece", Price = 4200m, TaxRate = 18m },
            new Product { Id = Guid.NewGuid(), UserId = user.Id, Name = "Site Inspection", Description = "On-site assessment and report.", Unit = "Visit", Price = 750m, TaxRate = 0m }
        };
        db.Products.AddRange(products);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var quotation = new Quotation
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            CustomerId = customers[0].Id,
            Sequence = 1,
            QuotationNumber = QuotationService.FormatNumber(1),
            QuotationDate = today,
            ValidUntil = today.AddDays(15),
            Status = QuotationStatus.Draft,
            Notes = "Thank you for your business. Work can begin within three days of approval.",
            Terms = "Quotation is valid until the specified date. 50% advance payable on confirmation, balance on completion. Prices include applicable taxes as shown."
        };

        quotation.Items.Add(new QuotationItem
        {
            Id = Guid.NewGuid(), QuotationId = quotation.Id, ProductId = products[0].Id, SortOrder = 0,
            Name = products[0].Name, Description = products[0].Description, Unit = products[0].Unit,
            Quantity = 2m, UnitPrice = 5000m, Discount = 500m, TaxRate = 18m
        });
        quotation.Items.Add(new QuotationItem
        {
            Id = Guid.NewGuid(), QuotationId = quotation.Id, ProductId = products[1].Id, SortOrder = 1,
            Name = products[1].Name, Description = products[1].Description, Unit = products[1].Unit,
            Quantity = 1m, UnitPrice = 1500m, Discount = 0m, TaxRate = 18m
        });

        QuotationCalculator.ApplyTotals(quotation);
        db.Quotations.Add(quotation);

        await db.SaveChangesAsync();
        logger.LogInformation("Seeded demo account {Email} / {Password}", DemoEmail, DemoPassword);
    }
}
