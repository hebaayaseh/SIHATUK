using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Sehatak.Domain.Entities.SharedEntities;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;
using Sehatak.Infrastructure.Services;
using Shouldly;

namespace Sehatak.IntegrationTests.Infrastructure;

/// <summary>
/// TenantMigrationRunner: applies pending tenant migrations to every center listed in the Shared DB.
/// Each test uses its OWN empty shared database (so the centers of other tests are not migrated again)
/// and its own range of center ids (so it never touches the tenant databases of other tests).
/// </summary>
[Collection(SqlServerCollection.Name)]
public class TenantMigrationRunnerTests(SqlServerFixture fx)
{
    private static int _range;

    private SharedDbContext? _shared;
    private int _nextId;
    private string _serverConnection = "";

    private TenantDbContextFactory Factory => fx.Harness.TenantFactory;

    /// <summary>Own shared DB whose center ids start in a private range.</summary>
    private async Task<TenantMigrationRunner> NewRunnerAsync()
    {
        var baseId = 800_000 + 100 * Interlocked.Increment(ref _range);
        var cs = new SqlConnectionStringBuilder(fx.Harness.NewSharedDb().Database.GetConnectionString())
        {
            InitialCatalog = $"runner_shared_{Guid.NewGuid():N}"
        };
        _serverConnection = new SqlConnectionStringBuilder(cs.ConnectionString) { InitialCatalog = "master" }.ConnectionString;
        _shared = new SharedDbContext(new DbContextOptionsBuilder<SharedDbContext>().UseSqlServer(cs.ConnectionString).Options);
        await _shared.Database.MigrateAsync();
        await _shared.Database.ExecuteSqlRawAsync($"DBCC CHECKIDENT ('medical_centers', RESEED, {baseId})");
        _nextId = baseId;
        return new TenantMigrationRunner(_shared, Factory);
    }

    private async Task<int> AddCenterAsync(CenterStatus status = CenterStatus.Active)
    {
        var c = new MedicalCenter
        {
            Name = "Runner Test Center", UniqueUrl = Guid.NewGuid().ToString("N"), Phone = "0590000000",
            Address = "Hebron", CenterStatus = status
        };
        _shared!.MedicalCenters.Add(c);
        await _shared.SaveChangesAsync();
        c.Id.ShouldBeGreaterThanOrEqualTo(_nextId);   // private id range, never the ids other tests use
        _nextId = c.Id + 1;
        return c.Id;
    }

    private static string DbName(int centerId) => $"sehatak_center_{centerId}";

    private async Task<bool> DatabaseExistsAsync(int centerId)
    {
        await using var conn = new SqlConnection(_serverConnection);
        await conn.OpenAsync();
        var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT CASE WHEN DB_ID('{DbName(centerId)}') IS NULL THEN 0 ELSE 1 END";
        return (int)(await cmd.ExecuteScalarAsync())! == 1;
    }

    /// <summary>A database that already has a "users" table but no migration history: the first migrations cannot apply.</summary>
    private async Task CreateBrokenTenantAsync(int centerId)
    {
        await using (var conn = new SqlConnection(_serverConnection))
        {
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE [{DbName(centerId)}]";
            await cmd.ExecuteNonQueryAsync();
        }
        var tenantCs = new SqlConnectionStringBuilder(_serverConnection) { InitialCatalog = DbName(centerId) }.ConnectionString;
        await using var c2 = new SqlConnection(tenantCs);
        await c2.OpenAsync();
        var cmd2 = c2.CreateCommand();
        cmd2.CommandText = "CREATE TABLE users (Id int NOT NULL)";
        await cmd2.ExecuteNonQueryAsync();
    }

    private static List<string> AllMigrations()
    {
        using var db = new TenantDbContext(new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlServer("Server=.;Database=x", o => o.MigrationsAssembly("Sehatak.Infrastructure")).Options);
        return db.Database.GetMigrations().ToList();
    }

    // ---------- listing ----------

    [Fact]
    public async Task The_list_has_every_center_whatever_its_status()
    {
        var runner = await NewRunnerAsync();
        var a = await AddCenterAsync(CenterStatus.Active);
        var p = await AddCenterAsync(CenterStatus.Pending);
        var s = await AddCenterAsync(CenterStatus.Suspended);

        var ids = await runner.GetAllCenterIdsAsync();

        ids.OrderBy(i => i).ShouldBe(new[] { a, p, s });
    }

    [Fact]
    public async Task With_no_centers_there_is_nothing_to_do()
    {
        var runner = await NewRunnerAsync();

        (await runner.GetAllCenterIdsAsync()).ShouldBeEmpty();
        (await runner.MigrateAllTenantsAsync()).ShouldBeEmpty();
    }

    // ---------- migrate all ----------

    [Fact]
    public async Task A_center_without_a_database_gets_one_with_every_migration_applied()
    {
        var runner = await NewRunnerAsync();
        var id = await AddCenterAsync();
        (await DatabaseExistsAsync(id)).ShouldBeFalse();

        var results = await runner.MigrateAllTenantsAsync();

        var r = results.Single();
        r.CenterId.ShouldBe(id);
        r.Success.ShouldBeTrue();
        r.ErrorMessage.ShouldBeNull();
        r.AppliedMigrations.ShouldBe(AllMigrations());
        (await DatabaseExistsAsync(id)).ShouldBeTrue();
        await using var db = fx.Harness.Tenant(id);
        (await db.Users.CountAsync()).ShouldBe(0);
        (await db.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Running_it_again_changes_nothing_and_reports_nothing_applied()
    {
        var runner = await NewRunnerAsync();
        var id = await AddCenterAsync();
        await runner.MigrateAllTenantsAsync();

        var second = (await runner.MigrateAllTenantsAsync()).Single();

        second.CenterId.ShouldBe(id);
        second.Success.ShouldBeTrue();
        second.AppliedMigrations.ShouldNotBeNull();
        second.AppliedMigrations.ShouldBeEmpty();
    }

    [Fact]
    public async Task Pending_and_suspended_centers_are_migrated_too_and_there_is_one_result_per_center_in_order()
    {
        var runner = await NewRunnerAsync();
        var a = await AddCenterAsync(CenterStatus.Active);
        var p = await AddCenterAsync(CenterStatus.Pending);
        var s = await AddCenterAsync(CenterStatus.Suspended);

        var results = await runner.MigrateAllTenantsAsync();

        results.Select(r => r.CenterId).OrderBy(i => i).ShouldBe(new[] { a, p, s });
        results.ShouldAllBe(r => r.Success);
    }

    /// <summary>The query has no ORDER BY, so SQL Server returns the centers in whatever order its index gives (here: by the random UniqueUrl).
    /// The report of a migration run should be in a stable order.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task The_results_come_in_center_id_order()
    {
        var runner = await NewRunnerAsync();
        var ids = new List<int>();
        for (var i = 0; i < 4; i++) ids.Add(await AddCenterAsync());

        var results = await runner.MigrateAllTenantsAsync();

        results.Select(r => r.CenterId).ShouldBe(ids);
        (await runner.GetAllCenterIdsAsync()).ShouldBe(ids);
    }

    [Fact]
    public async Task A_center_with_a_few_missing_migrations_only_gets_the_missing_ones()
    {
        var runner = await NewRunnerAsync();
        var id = await AddCenterAsync();
        var all = AllMigrations();
        all.Count.ShouldBeGreaterThan(1);
        await using (var cs = new SqlConnection(_serverConnection))
        {
            await cs.OpenAsync();
            var cmd = cs.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE [{DbName(id)}]";
            await cmd.ExecuteNonQueryAsync();
        }
        await using (var db = fx.Harness.Tenant(id))
            await db.GetService<IMigrator>().MigrateAsync(all[0]);

        var r = (await runner.MigrateAllTenantsAsync()).Single();

        r.Success.ShouldBeTrue();
        r.AppliedMigrations.ShouldBe(all.Skip(1).ToList());
    }

    [Fact]
    public async Task One_broken_center_is_reported_and_does_not_stop_the_others()
    {
        var runner = await NewRunnerAsync();
        var first = await AddCenterAsync();
        var broken = await AddCenterAsync();
        var last = await AddCenterAsync();
        await CreateBrokenTenantAsync(broken);

        var results = await runner.MigrateAllTenantsAsync();

        results.Select(r => r.CenterId).OrderBy(i => i).ShouldBe(new[] { first, broken, last });
        results.Single(r => r.CenterId == first).Success.ShouldBeTrue();
        var bad = results.Single(r => r.CenterId == broken);
        bad.Success.ShouldBeFalse();
        bad.ErrorMessage.ShouldNotBeNullOrWhiteSpace();
        results.Single(r => r.CenterId == last).Success.ShouldBeTrue();
        await using var db = fx.Harness.Tenant(last);
        (await db.Users.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task A_center_that_failed_can_be_fixed_and_migrated_on_the_next_run()
    {
        var runner = await NewRunnerAsync();
        var id = await AddCenterAsync();
        await CreateBrokenTenantAsync(id);
        (await runner.MigrateAllTenantsAsync()).Single().Success.ShouldBeFalse();

        // the owner drops the stray table
        var tenantCs = new SqlConnectionStringBuilder(_serverConnection) { InitialCatalog = DbName(id) }.ConnectionString;
        await using (var c = new SqlConnection(tenantCs))
        {
            await c.OpenAsync();
            var cmd = c.CreateCommand();
            cmd.CommandText = "DROP TABLE users";
            await cmd.ExecuteNonQueryAsync();
        }

        (await runner.MigrateAllTenantsAsync()).Single().Success.ShouldBeTrue();
    }

    // ---------- single center (the factory method the single-center endpoint calls) ----------

    [Fact]
    public async Task Migrating_one_center_creates_its_database_and_a_second_call_applies_nothing()
    {
        var runner = await NewRunnerAsync();
        var id = await AddCenterAsync();

        var first = await Factory.MigrateSingleTenantAsync(id);
        var second = await Factory.MigrateSingleTenantAsync(id);

        first.Success.ShouldBeTrue();
        first.AppliedMigrations.ShouldBe(AllMigrations());
        second.Success.ShouldBeTrue();
        second.AppliedMigrations.ShouldBeEmpty();
        (await runner.MigrateAllTenantsAsync()).Single().AppliedMigrations.ShouldBeEmpty();
    }
}
