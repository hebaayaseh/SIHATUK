
using Microsoft.EntityFrameworkCore;
using Sehatak.Infrastructure.Data;

namespace Sehatak.Infrastructure.Services;
    public class TenantMigrationRunner
    {
        private readonly SharedDbContext _sharedDb;
        private readonly TenantDbContextFactory _tenantFactory;

        public TenantMigrationRunner(SharedDbContext sharedDb, TenantDbContextFactory tenantFactory)
        {
            _sharedDb = sharedDb;
            _tenantFactory = tenantFactory;
        }

        public async Task<List<int>> GetAllCenterIdsAsync()
        {
            return await _sharedDb.MedicalCenters
                .Select(c => c.Id)
                .ToListAsync();
        }

        public async Task<List<TenantMigrationResult>> MigrateAllTenantsAsync()
        {
            var centerIds = await GetAllCenterIdsAsync();
            var results = new List<TenantMigrationResult>();

            foreach (var centerId in centerIds)
            {
                var result = await _tenantFactory.MigrateSingleTenantAsync(centerId);
                results.Add(result);
            }

            return results;
        }
    }
