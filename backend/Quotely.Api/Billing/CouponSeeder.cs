using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Quotely.Api.Data;
using Quotely.Api.Models;

namespace Quotely.Api.Billing;

/// <summary>
/// Creates the coupons Quotely ships with, once.
///
/// Idempotent by design: an existing code is left completely untouched, including its redemption
/// count and its active flag. A seeder that "updated" coupons on every start-up would reset
/// counts and silently revive codes that had been deliberately withdrawn — and on a platform
/// that restarts on every deploy, that is a coupon nobody can ever turn off.
///
/// This is not demo data, so it runs in production too. It creates one offer, not a customer or
/// a payment.
/// </summary>
public static class CouponSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>().CreateLogger(nameof(CouponSeeder));

        // A database that has not been migrated yet is not an error here — the deployment simply
        // has not reached that step. Seeding is skipped and tried again on the next start.
        //
        // CanConnect is not enough on its own: it answers "is the server reachable?", and a
        // reachable database with no schema still fails the read below. So the read itself is
        // what decides, and a missing table is treated as "not ready" rather than as a crash on
        // start-up.
        try
        {
            if (!await db.Database.CanConnectAsync()) return;
            if (await db.Coupons.AnyAsync(c => c.Code == WellKnownCoupons.SixMonthsFree)) return;
        }
        catch (DbException)
        {
            logger.LogInformation("Skipping coupon seeding: the schema is not ready yet.");
            return;
        }

        db.Coupons.Add(new Coupon
        {
            Id = Guid.NewGuid(),
            Code = WellKnownCoupons.SixMonthsFree,
            Description = "Six months of Quotely Pro, free.",
            FreeMonths = 6,
            // Unlimited accounts may redeem it, but each of them only once — which is the
            // (CouponId, UserId) unique index, not a number here.
            MaxRedemptions = null,
            ExpiresAt = null,
            IsActive = true
        });

        try
        {
            await db.SaveChangesAsync();
            logger.LogInformation("Seeded the {Code} coupon", WellKnownCoupons.SixMonthsFree);
        }
        catch (DbUpdateException)
        {
            // Two instances started at once and both tried. The unique index on Code settled it.
            logger.LogInformation("The {Code} coupon already exists", WellKnownCoupons.SixMonthsFree);
        }
    }
}
