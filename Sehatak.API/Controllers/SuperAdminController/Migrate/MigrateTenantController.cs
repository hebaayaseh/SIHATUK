using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Infrastructure.Data;
using Sehatak.Infrastructure.Services;

namespace Sehatak.API.Controllers.SuperAdminController.Migrate
{
    [ApiController]
    [Route("api/[Controller]")]
    public class MigrateTenantController : ControllerBase
    {
        private readonly TenantMigrationRunner _migrationRunner;
        private readonly TenantDbContextFactory dbContextFactory;

        public MigrateTenantController(TenantMigrationRunner migrationRunner, TenantDbContextFactory dbContextFactory)
        {
            _migrationRunner = migrationRunner;
            this.dbContextFactory = dbContextFactory;
        }

        [Authorize(Policy = "SuperAdminOnly")]
        [HttpPost("migrate-all-tenants")]
        public async Task<IActionResult> MigrateAllTenants()
        {
            var results = await _migrationRunner.MigrateAllTenantsAsync();

            var succeeded = results.Where(r => r.Success).ToList();
            var failed = results.Where(r => !r.Success).ToList();

            return Ok(new
            {
                totalCenters = results.Count,
                succeededCount = succeeded.Count,
                failedCount = failed.Count,
                succeeded = succeeded.Select(r => new { r.CenterId, r.AppliedMigrations }),
                failed = failed.Select(r => new { r.CenterId, r.ErrorMessage })
            });
        }

        [Authorize(Policy = "SuperAdminOnly")]
        [HttpPost("migrate-tenant/{centerId}")]
        public async Task<IActionResult> MigrateSingleTenant(int centerId)
        {
            var result = await dbContextFactory.MigrateSingleTenantAsync(centerId);
            return Ok(result);
        }
    }
}
