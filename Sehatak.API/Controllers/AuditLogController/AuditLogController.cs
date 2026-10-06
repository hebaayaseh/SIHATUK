using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.AuditLogDto;
using Sehatak.Application.Interfaces.AuditLog;

namespace Sehatak.API.Controllers.AuditLogController
{
    [ApiController]
    [Route("[Controller]")]
    public class AuditLogController : ControllerBase
    {
        private readonly IAuditLog auditLog;
        public AuditLogController(IAuditLog auditLog)
        {
            this.auditLog = auditLog;
        }

        [Authorize(Roles = "AdminOnly")]
        [HttpGet("admin-get-audit-logs/{centerId}")]
        public async Task<IActionResult> GetAuditLogs(int centerId, [FromQuery] AuditLogRequestDto request, [FromQuery] PagedRequest paged)
        {
            var logs = await auditLog.GetAuditLogsAsync(centerId,request,paged);
            return Ok(logs);
        }

        [Authorize(Policy = "AdminOnly")]
        [HttpGet("admin-get-failed-login-attempts/{centerId}")]
        public async Task<IActionResult> GetFailedLoginAttempts(int centerId, [FromQuery] PagedRequest paged)
        {
            var result = await auditLog.GetFailedLoginAttemptsAsync(centerId, paged);
            return Ok(result);
        }

        [Authorize(Policy = "SuperAdminOnly")]
        [HttpGet("superAdmin-get-failed-login-attempts")]
        public async Task<IActionResult> GetAllFailedLoginAttempts([FromQuery] int? centerId, [FromQuery] PagedRequest paged)
        {
            var result = await auditLog.GetAllFailedLoginAttemptsAsync(centerId, paged);
            return Ok(result);
        }

        [Authorize(Policy = "AdminOnly")]
        [HttpGet("admin-get-failed-staff-login-attempts/{centerId}")]
        public async Task<IActionResult> GetFailedStaffLoginAttempts(int centerId, [FromQuery] PagedRequest paged)
        {
            var result = await auditLog.GetFailedStaffLoginAttemptsAsync(centerId, paged);
            return Ok(result);
        }
    }
}
