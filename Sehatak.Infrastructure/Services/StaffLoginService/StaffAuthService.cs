using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.DTOs.StaffLogIn;
using Sehatak.Application.Interfaces.AuditLog;
using Sehatak.Application.Interfaces.IAuth;
using Sehatak.Application.Interfaces.IEmail;
using Sehatak.Application.Interfaces.StaffLogin;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;

namespace Sehatak.Infrastructure.Services.StaffLogin
{
    public class StaffAuthService : IStaffLogin
    {
        private readonly TenantDbContextFactory contextFactory;
        private readonly IEmailService emailService;
        private readonly SharedDbContext sharedDbContext;
        private readonly ITokenService tokenService;
        private readonly IAuditLog auditLog;
        public StaffAuthService(TenantDbContextFactory contextFactory, IEmailService emailService, SharedDbContext sharedDbContext, ITokenService tokenService , IAuditLog auditLog)
        {
            this.contextFactory = contextFactory;
            this.emailService = emailService;
            this.sharedDbContext = sharedDbContext;
            this.tokenService = tokenService;
            this.auditLog = auditLog;
        }

        public async Task<StaffLoginResponseDto> StaffLoginAsync(int centerId, StaffLoginRequestDto request, string? ipAddress)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            await auditLog.EnsureNotLockedOutAsync(centerId, request.Email, LoginUserType.Staff);

            using var db = contextFactory.CreateForCenter(centerId);

            var user = await db.Users.FirstOrDefaultAsync(u => u.email == request.Email && u.isActive);
            if (user == null)
            {
                await auditLog.RecordLoginAttemptAsync(centerId, request.Email, ipAddress, success: false, LoginUserType.Staff);
                throw new BusinessException("Auth.Unauthorized");
            }

            if (center.CenterStatus == CenterStatus.Suspended && user.role != userRole.Admin)
            {
                await auditLog.RecordLoginAttemptAsync(centerId, request.Email, ipAddress, success: false, LoginUserType.Staff);
                throw new BusinessException("Center.Suspended");
            }

            var valid = BCrypt.Net.BCrypt.Verify(request.Password, user.passwordHash);

            if (!valid)
            {
                await auditLog.RecordLoginAttemptAsync(centerId, request.Email, ipAddress, success: false, LoginUserType.Staff);
                throw new BusinessException("Validation.PasswordMismatch");
            }

            if (user.role == userRole.Patient)
            {
                await auditLog.RecordLoginAttemptAsync(centerId, request.Email, ipAddress, success: false, LoginUserType.Staff);
                throw new BusinessException("Auth.Forbidden");
            }

            await auditLog.RecordLoginAttemptAsync(centerId, request.Email, ipAddress, success: true, LoginUserType.Staff);

            var tokens = await tokenService.IssueTokensAsync(
                userId: user.Id,
                name: $"{user.firstName}{user.lastName}",
                email: user.email,
                role: user.role.ToString(),
                centerId: centerId,
                ownerType: TokenOwnerType.TenantUser
            );
            return new StaffLoginResponseDto
            {
                AccessToken = tokens.AccessToken,
                RefreshToken = tokens.RefreshToken
            };

        }
    }
}
