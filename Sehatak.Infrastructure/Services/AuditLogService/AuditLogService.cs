using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.AuditLogDto;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.Interfaces.AuditLog;
using Sehatak.Domain.Entities;
using Sehatak.Domain.Entities.General;
using Sehatak.Domain.Entities.SharedEntities;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;
using System.Security.Claims;
using System.Text.Json;


namespace Sehatak.Infrastructure.Services.AuditLogService;
public class AuditLogService : IAuditLog
{
    private readonly SharedDbContext _sharedDbContext;
    private readonly TenantDbContextAccessor _tenantDbAccessor;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutWindow = TimeSpan.FromMinutes(15);
    public AuditLogService(
        TenantDbContextAccessor tenantDbAccessor,
        IHttpContextAccessor httpContextAccessor,
        SharedDbContext sharedDbContext)
    {
        _tenantDbAccessor = tenantDbAccessor;
        _httpContextAccessor = httpContextAccessor;
        _sharedDbContext = sharedDbContext;
    }

    public async Task<PagedResult<AuditLogResponseDto>> GetAuditLogsAsync(int centerId, AuditLogRequestDto request, PagedRequest paged)
    {
        var center = await _sharedDbContext.MedicalCenters
            .FirstOrDefaultAsync(c => c.Id == centerId
                                 && c.CenterStatus == CenterStatus.Active);

        if (center == null)
            throw new BusinessException("Center.NotFound");

        using var db = _tenantDbAccessor.GetCurrentTenantDb();

        var query = db.AuditLogs
            .Where(log => request.EntityType == null || log.EntityType == request.EntityType)
            .OrderByDescending(log => log.CreatedAt)
            .Select(log => new AuditLogResponseDto
            {
                Id = log.Id,
                UserId = log.UserId,
                Action = log.Action,
                EntityName = log.EntityType,
                EntityId = log.EntityId,
                OldValue = log.OldValue,
                NewValue = log.NewValue,
                CreatedAt = log.CreatedAt
            });

        return await query.ToPagedResultAsync(paged.PageNumber, paged.PageSize);
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
    public Sehatak.Domain.Entities.General.AuditLog? Build(string action, string entityType, int? entityId, object? oldValue = null, object? newValue = null)
    {
        var userIdClaim = _httpContextAccessor.HttpContext?.User
            .FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (userIdClaim == null) return null;

        return new Sehatak.Domain.Entities.General.AuditLog
        {
            UserId = int.Parse(userIdClaim),
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            OldValue = oldValue != null ? JsonSerializer.Serialize(oldValue) : null,
            NewValue = newValue != null ? JsonSerializer.Serialize(newValue) : null,
            CreatedAt = DateTime.UtcNow
        };
    }

    public async Task EnsureNotLockedOutAsync(int centerId, string email, LoginUserType userType)
    {
        var windowStart = DateTime.UtcNow - LockoutWindow;

        var failedCount = await _sharedDbContext.LoginAttempts
            .Where(a => a.CenterId == centerId
                     && a.Email == email
                     && a.UserType == userType
                     && !a.Success
                     && a.AttemptedAt >= windowStart)
            .CountAsync();

        if (failedCount >= MaxFailedAttempts)
            throw new BusinessException("Auth.TooManyAttempts");
    }

    public async Task RecordLoginAttemptAsync(int centerId, string email, string? ipAddress, bool success, LoginUserType userType)
    {
        _sharedDbContext.LoginAttempts.Add(new LoginAttempt
        {
            CenterId = centerId,
            Email = email,
            IpAddress = ipAddress,
            Success = success,
            UserType = userType,
            AttemptedAt = DateTime.UtcNow
        });

        await _sharedDbContext.SaveChangesAsync();
    }

    public async Task<PagedResult<LoginAttemptResponseDto>> GetFailedLoginAttemptsAsync(int centerId, PagedRequest paged)
    {
        var query = _sharedDbContext.LoginAttempts
            .Where(a => a.CenterId == centerId && !a.Success)
            .OrderByDescending(a => a.AttemptedAt)
            .Select(a => new LoginAttemptResponseDto
            {
                Id = a.Id,
                CenterId = a.CenterId,
                Email = a.Email,
                IpAddress = a.IpAddress,
                UserType = a.UserType,
                AttemptedAt = a.AttemptedAt
            });

        return await query.ToPagedResultAsync(paged.PageNumber, paged.PageSize);
    }

    public async Task<PagedResult<LoginAttemptResponseDto>> GetAllFailedLoginAttemptsAsync(int? centerId, PagedRequest paged)
    {
        var query = _sharedDbContext.LoginAttempts
            .Where(a => !a.Success && (centerId == null || a.CenterId == centerId))
            .OrderByDescending(a => a.AttemptedAt)
            .Select(a => new LoginAttemptResponseDto
            {
                Id = a.Id,
                CenterId = a.CenterId,
                Email = a.Email,
                IpAddress = a.IpAddress,
                UserType = a.UserType,
                AttemptedAt = a.AttemptedAt
            });

        return await query.ToPagedResultAsync(paged.PageNumber, paged.PageSize);
    }

    public async Task<PagedResult<LoginAttemptResponseDto>> GetFailedStaffLoginAttemptsAsync(int centerId, PagedRequest paged)
    {
        var query = _sharedDbContext.LoginAttempts
            .Where(a => a.CenterId == centerId && !a.Success && a.UserType == LoginUserType.Staff)
            .OrderByDescending(a => a.AttemptedAt)
            .Select(a => new LoginAttemptResponseDto
            {
                Id = a.Id,
                CenterId = a.CenterId,
                Email = a.Email,
                IpAddress = a.IpAddress,
                UserType = a.UserType,
                AttemptedAt = a.AttemptedAt
            });

        return await query.ToPagedResultAsync(paged.PageNumber, paged.PageSize);
    }
}
