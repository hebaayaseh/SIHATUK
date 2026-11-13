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

        private const int MaxFailedAttempts = 5;
        private static readonly TimeSpan LockoutWindow = TimeSpan.FromMinutes(15);

        public async Task<SuperAdminLoginResponseDto?> LoginAsync(SuperAdminLoginRequestDto request)
        {
            var windowStart = DateTime.UtcNow - LockoutWindow;
            var failed = await sharedDbContext.LoginAttempts
                .CountAsync(a => a.CenterId == 0
                              && a.Email == request.email
                              && a.UserType == LoginUserType.SuperAdmin
                              && !a.Success
                              && a.AttemptedAt >= windowStart);

            if (failed >= MaxFailedAttempts)
                throw new BusinessException("Auth.TooManyAttempts");

            var superAdmin = await sharedDbContext.SuperAdmins
                .FirstOrDefaultAsync(e => e.Email == request.email);

            var ok = superAdmin != null
                     && superAdmin.IsActive
                     && BCrypt.Net.BCrypt.Verify(request.password, superAdmin.PasswordHash);

            sharedDbContext.LoginAttempts.Add(new LoginAttempt
            {
                CenterId = 0,
                Email = request.email,
                Success = ok,
                UserType = LoginUserType.SuperAdmin,
                AttemptedAt = DateTime.UtcNow
            });
            await sharedDbContext.SaveChangesAsync();

            if (!ok) return null;
            superAdmin = superAdmin!;

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
                throw new BusinessException("Auth.EmailExists");

            var phoneExists = await sharedDbContext.SuperAdmins.AnyAsync(e => e.phone == request.phoneNumber);
            if (phoneExists)
                throw new BusinessException("Auth.PhoneExists");
            
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
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png" };
                var extension = Path.GetExtension(request.ProfileImageUrl.FileName).ToLower();
                if (!allowedExtensions.Contains(extension))
                    throw new BusinessException("Validation.InvalidFileType");

                if (request.ProfileImageUrl.Length > 5 * 1024 * 1024)
                    throw new BusinessException("Validation.FileTooLarge");

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
