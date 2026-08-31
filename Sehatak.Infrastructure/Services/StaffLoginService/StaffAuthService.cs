using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.DTOs.StaffLogIn;
using Sehatak.Application.Interfaces.IAuth;
using Sehatak.Application.Interfaces.IEmail;
using Sehatak.Application.Interfaces.StaffLogin;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;
using Sehatak.Infrastructure.Security;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Infrastructure.Services.StaffLogin
{
    public class StaffAuthService : IStaffLogin
    {
        private readonly TenantDbContextFactory contextFactory;
        private readonly IEmailService emailService;
        private readonly SharedDbContext sharedDbContext;
        private readonly ITokenService tokenService;
        public StaffAuthService(TenantDbContextFactory contextFactory, IEmailService emailService, SharedDbContext sharedDbContext, ITokenService tokenService)
        {
            this.contextFactory = contextFactory;
            this.emailService = emailService;
            this.sharedDbContext = sharedDbContext;
            this.tokenService = tokenService;
        }

        public async Task<StaffLoginResponseDto> StaffLoginAsync(int CenterId, StaffLoginRequestDto request)
        {
            var center = await sharedDbContext.MedicalCenters
                .FindAsync(CenterId);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(CenterId);

            var user =  db.Users.FirstOrDefault(u => u.email == request.Email && u.isActive);
            if (user == null)
                throw new BusinessException("Auth.Unauthorized");

            if (center.CenterStatus == CenterStatus.Suspended && user.role != userRole.Admin)
                throw new BusinessException("Center.Suspended");

            var valid = BCrypt.Net.BCrypt.Verify(request.Password, user.passwordHash);

            if (!valid)
                throw new BusinessException("Validation.PasswordMismatch");

            if (user.role == userRole.Patient)
                throw new BusinessException("Auth.Forbidden");

            var tokens = await tokenService.IssueTokensAsync(
                userId: user.Id,
                name: $"{user.firstName}{user.lastName}",
                email: user.email,
                role: user.role.ToString(),
                centerId: CenterId,
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
