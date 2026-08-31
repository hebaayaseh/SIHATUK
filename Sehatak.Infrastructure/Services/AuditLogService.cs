
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Sehatak.Application.Interfaces.AuditLog;
using Sehatak.Domain.Entities;
using Sehatak.Domain.Entities.General;
using Sehatak.Infrastructure.Data;


namespace Sehatak.Infrastructure.Services;
    public class AuditLogService : IAuditLog
    {
        private readonly TenantDbContextAccessor _tenantDbAccessor;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public AuditLogService(
            TenantDbContextAccessor tenantDbAccessor,
            IHttpContextAccessor httpContextAccessor)
        {
            _tenantDbAccessor = tenantDbAccessor;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task LogAsync(string action, string entityType, int? entityId, object? oldValue = null, object? newValue = null)
        {
            var userIdClaim = _httpContextAccessor.HttpContext?.User
                .FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (userIdClaim == null) return;

            using var db = _tenantDbAccessor.GetCurrentTenantDb();

            var log = new AuditLog
            {
                UserId = int.Parse(userIdClaim),
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                OldValue = oldValue != null ? JsonSerializer.Serialize(oldValue) : null,
                NewValue = newValue != null ? JsonSerializer.Serialize(newValue) : null,
                CreatedAt = DateTime.UtcNow
            };

            await db.AuditLogs.AddAsync(log);
            await db.SaveChangesAsync();   // لازم تحفظ هون لأنه DbContext منفصل عن اللي بالكنترولر
        }
    }
