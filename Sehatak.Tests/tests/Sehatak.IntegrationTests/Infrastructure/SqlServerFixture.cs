using Testcontainers.MsSql;

namespace Sehatak.IntegrationTests.Infrastructure;

/// <summary>
/// One real SQL Server container shared by every test class in the "SqlServer" collection.
/// Real SQL Server (not EF InMemory) because the schema relies on filtered unique indexes,
/// transactions and ExecuteUpdate. Requires Docker.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    public TenantTestHarness Harness { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        Harness = await TenantTestHarness.CreateAsync(_container.GetConnectionString());
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition(Name)]
public class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "SqlServer";
}
