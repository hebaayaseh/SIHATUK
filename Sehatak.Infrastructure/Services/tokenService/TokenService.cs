using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.Auth;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.Interfaces.IAuth;
using Sehatak.Domain.Entities.SharedEntities;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;
using Sehatak.Infrastructure.Security;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Infrastructure.Services.tokenService
{
    public class TokenService : ITokenService
    {
        private readonly SharedDbContext sharedDbContext;
        private readonly TenantDbContextFactory tenantFactory;
        private readonly JwtTokenGenerator jwtGenerator;

        public TokenService(SharedDbContext sharedDbContext, TenantDbContextFactory tenantFactory, JwtTokenGenerator jwtGenerator)
        {
            this.sharedDbContext = sharedDbContext;
            this.tenantFactory = tenantFactory;
            this.jwtGenerator = jwtGenerator;
        }

        public async Task<TokenResponseDto> IssueTokensAsync(int userId, string name, string email, string role, int? centerId, TokenOwnerType ownerType)
        {
            var accessToken = jwtGenerator.GenerateToken(userId, name, email, role, centerId);
            var refreshTokenValue = GenerateSecureToken();

            sharedDbContext.RefreshTokens.Add(new RefreshToken {
                Token = refreshTokenValue,
                OwnerType = ownerType,
                UserId = userId,
                CenterId = centerId,
                ExpiresAt = DateTime.UtcNow.AddDays(7)
            });

            await sharedDbContext.SaveChangesAsync();

            return new TokenResponseDto 
            { 
                AccessToken = accessToken,
                RefreshToken = refreshTokenValue
            };
        }

        private string GenerateSecureToken()
        {
            var randomBytes = new byte[64];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(randomBytes);
            return Convert.ToBase64String(randomBytes);

        }

        public async Task LogoutAsync(string refreshToken)
        {
            var existing = await sharedDbContext.RefreshTokens
                .FirstOrDefaultAsync(t => t.Token == refreshToken);

            if (existing != null)
            {
                existing.IsRevoked = true;
                await sharedDbContext.SaveChangesAsync();
            }
        }

        public async Task<TokenResponseDto> RefreshAsync(string refreshToken)
        {
            var existing = await sharedDbContext.RefreshTokens
                .FirstOrDefaultAsync(t => t.Token == refreshToken);

            if (existing == null || existing.IsRevoked || existing.ExpiresAt < DateTime.UtcNow)
                throw new BusinessException("Auth.InvalidRefreshToken");

            string name, email, role;

            if (existing.OwnerType == TokenOwnerType.SuperAdmin)
            {
                var superAdmin = await sharedDbContext.SuperAdmins.FindAsync(existing.UserId);
                if (superAdmin == null || !superAdmin.IsActive)
                    throw new BusinessException("Auth.InvalidRefreshToken");

                name = superAdmin.Name;
                email = superAdmin.Email;
                role = superAdmin.role.ToString();
            }
            else
            {
                if (existing.CenterId == null)
                    throw new BusinessException("Auth.InvalidRefreshToken");

                using var tenantDb = tenantFactory.CreateForCenter(existing.CenterId.Value);
                var user = await tenantDb.Users.FindAsync(existing.UserId);
                if (user == null || !user.isActive)
                    throw new BusinessException("Auth.InvalidRefreshToken");

                name = $"{user.firstName} {user.lastName}";
                email = user.email!;
                role = user.role.ToString();
            }

            var newRefreshTokenValue = GenerateSecureToken();

            await using var tx = await sharedDbContext.Database.BeginTransactionAsync();

            // نحجز التوكن بالـ SQL: إذا طلبين وصلوا مع بعض، واحد بس بينجح
            var claimed = await sharedDbContext.RefreshTokens
                .Where(t => t.Id == existing.Id && !t.IsRevoked)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.IsRevoked, true));
            if (claimed == 0)
                throw new BusinessException("Auth.InvalidRefreshToken");

            sharedDbContext.RefreshTokens.Add(new RefreshToken
            {
                Token = newRefreshTokenValue,
                OwnerType = existing.OwnerType,
                UserId = existing.UserId,
                CenterId = existing.CenterId,
                ExpiresAt = DateTime.UtcNow.AddDays(7)
            });
            await sharedDbContext.SaveChangesAsync();
            await tx.CommitAsync();

            var newAccessToken = jwtGenerator.GenerateToken(existing.UserId, name, email, role, existing.CenterId);

            return new TokenResponseDto
            {
                AccessToken = newAccessToken,
                RefreshToken = newRefreshTokenValue
            };
        }
    }
}
