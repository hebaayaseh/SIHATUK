using Microsoft.EntityFrameworkCore;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests;

[Collection(SqlServerCollection.Name)]
public class SmokeTests(SqlServerFixture fx)
{
    [Fact]
    public async Task Tenant_database_is_created_and_migrated()
    {
        var centerId = await fx.Harness.CreateTenantAsync();

        await using var db = fx.Harness.Tenant(centerId);
        (await db.Users.CountAsync()).ShouldBe(0);
        (await db.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Two_tenants_are_isolated_databases()
    {
        var a = await fx.Harness.CreateTenantAsync();
        var b = await fx.Harness.CreateTenantAsync();

        a.ShouldNotBe(b);
        await using var dbA = fx.Harness.Tenant(a);
        await using var dbB = fx.Harness.Tenant(b);
        dbA.Database.GetDbConnection().Database.ShouldNotBe(dbB.Database.GetDbConnection().Database);
    }

    [Fact]
    public async Task Double_booking_guard_index_exists_with_expected_filter()
    {
        // The unique filtered index on Appointments(doctorId, appointmentDate, timeSlot) is the
        // only thing that stops two patients taking the same slot under concurrency.
        var centerId = await fx.Harness.CreateTenantAsync();
        await using var db = fx.Harness.Tenant(centerId);

        var filters = await db.Database.SqlQueryRaw<string>(
            @"SELECT i.filter_definition AS Value
              FROM sys.indexes i
              WHERE i.object_id = OBJECT_ID('Appointments')
                AND i.is_unique = 1 AND i.filter_definition IS NOT NULL").ToListAsync();

        filters.ShouldContain(f => f.Contains("timeSlot") && f.Contains("Confirmed") && f.Contains("Pending"));
    }

    [Fact]
    public async Task Shared_database_is_migrated()
    {
        await using var shared = fx.Harness.NewSharedDb();
        (await shared.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
    }
}
