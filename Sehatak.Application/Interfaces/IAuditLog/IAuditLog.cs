

using Sehatak.Application.Common;
using Sehatak.Application.DTOs.AuditLogDto;
using Sehatak.Domain.Enums.SharedEnums;

namespace Sehatak.Application.Interfaces.AuditLog
{
    public interface IAuditLog
    {
        Task LogAsync(string action, string entityType, int? entityId, object? oldValue = null, object? newValue = null);
        Sehatak.Domain.Entities.General.AuditLog? Build(string action, string entityType, int? entityId, object? oldValue = null, object? newValue = null);

        Task<PagedResult<AuditLogResponseDto>> GetAuditLogsAsync(int centerId,AuditLogRequestDto request, PagedRequest paged);
        Task EnsureNotLockedOutAsync(int centerId, string email, LoginUserType userType);
        Task RecordLoginAttemptAsync(int centerId, string email, string? ipAddress, bool success, LoginUserType userType);

        Task<PagedResult<LoginAttemptResponseDto>> GetFailedLoginAttemptsAsync(int centerId, PagedRequest paged);
        Task<PagedResult<LoginAttemptResponseDto>> GetAllFailedLoginAttemptsAsync(int? centerId, PagedRequest paged);
        Task<PagedResult<LoginAttemptResponseDto>> GetFailedStaffLoginAttemptsAsync(int centerId, PagedRequest paged);
    }
}
