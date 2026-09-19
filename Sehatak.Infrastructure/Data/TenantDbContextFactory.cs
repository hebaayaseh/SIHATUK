using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MySqlConnector;

namespace Sehatak.Infrastructure.Data
{
    public class TenantDbContextFactory
    {
        private readonly IConfiguration _config;

        public TenantDbContextFactory(IConfiguration config)
        {
            _config = config;
        }

        // بتبني TenantDbContext لمركز معين بناءً على الـ CenterId
        public TenantDbContext CreateForCenter(int centerId)
        {
            var connectionString = BuildConnectionString(centerId);
            var optionsBuilder = new DbContextOptionsBuilder<TenantDbContext>();
            optionsBuilder.UseMySql(
                connectionString,
                TenantServerVersionCache.Get(connectionString),
                mySqlOptions => mySqlOptions.MigrationsAssembly("Sehatak.Infrastructure")
            );
            return new TenantDbContext(optionsBuilder.Options);
        }

        public async Task CreateTenantDatabaseAsync(int centerId)
        {
            var connectionString = BuildConnectionString(centerId);
            await EnsureDatabaseCreated(connectionString);
            using var context = CreateForCenter(centerId);
            var pending = await context.Database.GetPendingMigrationsAsync();
            await context.Database.MigrateAsync();
        }

        // بتطبق الميجريشن على مركز واحد وترجع نتيجة واضحة
        public async Task<TenantMigrationResult> MigrateSingleTenantAsync(int centerId)
        {
            try
            {
                var connectionString = BuildConnectionString(centerId);
                await EnsureDatabaseCreated(connectionString);

                using var context = CreateForCenter(centerId);
                var pending = (await context.Database.GetPendingMigrationsAsync()).ToList();
                await context.Database.MigrateAsync();

                return new TenantMigrationResult
                {
                    CenterId = centerId,
                    Success = true,
                    AppliedMigrations = pending
                };
            }
            catch (Exception ex)
            {
                return new TenantMigrationResult
                {
                    CenterId = centerId,
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        private string BuildConnectionString(int centerId)
        {
            var dbName = $"sehatak_center_{centerId}";
            var template = _config.GetConnectionString("TenantDbTemplate");
            if (string.IsNullOrEmpty(template))
                throw new Exception("TenantDbTemplate is missing");
            return $"{template}Database={dbName};";
        }

        private async Task EnsureDatabaseCreated(string connectionString)
        {
            var builder = new MySqlConnectionStringBuilder(connectionString);
            var databaseName = builder.Database;
            builder.Database = "";
            using var connection = new MySqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE IF NOT EXISTS `{databaseName}`;";
            await command.ExecuteNonQueryAsync();
        }
    }

    public class TenantMigrationResult
    {
        public int CenterId { get; set; }
        public bool Success { get; set; }
        public List<string>? AppliedMigrations { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
