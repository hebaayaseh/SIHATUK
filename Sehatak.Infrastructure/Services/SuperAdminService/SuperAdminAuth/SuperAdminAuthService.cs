
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.DTOs.SuperAdminDto;
using Sehatak.Application.Interfaces.IAuth;
using Sehatak.Application.Interfaces.SuperAdminInterface;
using Sehatak.Domain.Entities.SharedEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;
using Sehatak.Infrastructure.Security;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EmailVerificationCode = Sehatak.Domain.Entities.General.EmailVerificationCode;

namespace Sehatak.Infrastructure.Services.SuperAdminService.SuperAdminAuth
{
    public class SuperAdminAuthService : ISuperAdminAuth
    {
        private readonly SharedDbContext sharedDbContext;
        private readonly ITokenService tokenService;
        private readonly IConfiguration configuration;
        public SuperAdminAuthService(SharedDbContext sharedDbContext, ITokenService tokenService, IConfiguration configuration)
        {
            this.sharedDbContext = sharedDbContext;
            this.tokenService = tokenService;
            this.configuration = configuration;
        }

        public async Task<SuperAdminLoginResponseDto?> LoginAsync(SuperAdminLoginRequestDto request)
        {
            var superAdmin = await sharedDbContext.SuperAdmins
                .FirstOrDefaultAsync(e => e.Email == request.email);

            if (superAdmin == null) return null;
            if (!superAdmin.IsActive) return null;

            var passwordValid = BCrypt.Net.BCrypt.Verify(request.password, superAdmin.PasswordHash);
            if (!passwordValid) return null;

            var tokens = await tokenService.IssueTokensAsync(
                userId: superAdmin.Id,
                name: superAdmin.Name,
                email: superAdmin.Email,
                role: superAdmin.role.ToString(),
                centerId: null,
                ownerType: TokenOwnerType.SuperAdmin
            );
            return new SuperAdminLoginResponseDto 
            {
                AccessToken = tokens.AccessToken,
                RefreshToken = tokens.RefreshToken 
            };
        }

        public async Task<RegisterSuperAdminResponseDto> RegisterAsync(RegisterSuperAdminRequestDto request)
        {
            var expectedKey = configuration["SuperAdminSetup:SetupKey"];

            if (string.IsNullOrEmpty(expectedKey) || expectedKey != request.SuperAdminKey)
                throw new UnauthorizedAccessException();

            var emailExists = await sharedDbContext.SuperAdmins.AnyAsync(e => e.Email == request.email);
            if (emailExists)
                throw new BusinessException("Auth.Forbidden");
            
            var superAdmin = new SuperAdmin
            {
                Email = request.email,
                role = userRole.SuperAdmin,
                Name = request.name,
                phone = request.phoneNumber,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.password),
                CreateAt = DateTime.UtcNow,
                IsActive = true
            };
            if(request.ProfileImageUrl != null)
            {

                var fileName = Guid.NewGuid() + Path.GetExtension(request.ProfileImageUrl.FileName);

                var path = Path.Combine("wwwroot/uploads/profileImage", fileName);

                using (var stream = new FileStream(path, FileMode.Create))
                {
                    await request.ProfileImageUrl.CopyToAsync(stream);
                }

                superAdmin.ProfileImageUrl = $"/uploads/profileImage/{fileName}";
            }

            await sharedDbContext.AddAsync(superAdmin);
            await sharedDbContext.SaveChangesAsync();

            return new RegisterSuperAdminResponseDto { email = superAdmin.Email };
            
        }

    }
}
