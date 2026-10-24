using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Sehatak.Domain.Entities.SharedEntities;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;

namespace Sehatak.IntegrationTests.Infrastructure;

/// <summary>
/// Points the production TenantDbContextFactory at the test container, so services run
/// unmodified: they ask for sehatak_center_{id} and get a real, migrated database.
/// Each test builds its own center (unique id) so tests never share data.
/// </summary>
public sealed class TenantTestHarness
{
    private static int _nextCenterId = 1000;

    public IConfiguration Config { get; }
    public FakePushNotification Push { get; } = new();
    public TenantDbContextFactory TenantFactory { get; }
    private readonly string _sharedConnectionString;

    private TenantTestHarness(string serverConnectionString)
    {
        var sb = new SqlConnectionStringBuilder(serverConnectionString) { InitialCatalog = "" };
        sb.Remove("Initial Catalog");
        var template = sb.ConnectionString.TrimEnd(';') + ";";   // factory appends "Database=sehatak_center_{id};"

        var shared = new SqlConnectionStringBuilder(serverConnectionString) { InitialCatalog = "sihatuk_shared_test" };
        _sharedConnectionString = shared.ConnectionString;

        Config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:TenantDbTemplate"] = template,
            ["ConnectionStrings:SharedDb"] = _sharedConnectionString,
            ["Jwt:SecretKey"] = "integration-test-secret-key-at-least-32-chars!!",
            ["Jwt:Issuer"] = "sehatak-test",
            ["Jwt:Audience"] = "sehatak-test-clients"
        }).Build();

        TenantFactory = new TenantDbContextFactory(Config, Push);
    }

    public static async Task<TenantTestHarness> CreateAsync(string serverConnectionString)
    {
        var h = new TenantTestHarness(serverConnectionString);
        await using var shared = h.NewSharedDb();
        await shared.Database.MigrateAsync();     // also proves the Shared migrations apply on an empty server
        return h;
    }

    public SharedDbContext NewSharedDb() =>
        new(new DbContextOptionsBuilder<SharedDbContext>().UseSqlServer(_sharedConnectionString).Options);

    /// <summary>Creates and migrates a fresh tenant database; returns its center id.</summary>
    public async Task<int> CreateTenantAsync()
    {
        var centerId = Interlocked.Increment(ref _nextCenterId);
        var result = await TenantFactory.MigrateSingleTenantAsync(centerId);
        if (!result.Success)
            throw new InvalidOperationException($"Tenant migration failed for center {centerId}: {result.ErrorMessage}");
        return centerId;
    }

    /// <summary>
    /// Creates a real center: a row in the Shared DB (its identity Id decides the tenant DB name,
    /// exactly like production) plus the migrated tenant database, seeded with an admin and a department.
    /// </summary>
    public async Task<TestCenter> CreateCenterAsync(CenterStatus status = CenterStatus.Active)
    {
        int id;
        await using (var shared = NewSharedDb())
        {
            var center = new MedicalCenter
            {
                Name = "Test Center", UniqueUrl = Guid.NewGuid().ToString("N"),
                Phone = "0590000000", Address = "Hebron", CenterStatus = status
            };
            shared.MedicalCenters.Add(center);
            await shared.SaveChangesAsync();
            id = center.Id;
        }

        var result = await TenantFactory.MigrateSingleTenantAsync(id);
        if (!result.Success)
            throw new InvalidOperationException($"Tenant migration failed for center {id}: {result.ErrorMessage}");

        var tc = new TestCenter(this, id);
        await tc.InitAsync();
        return tc;
    }

    public TenantDbContext Tenant(int centerId) => TenantFactory.CreateForCenter(centerId);
}
