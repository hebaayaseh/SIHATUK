using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.EditProfile.EditEmailOrPasswored;
using Sehatak.Application.DTOs.EditProfile.EditSuperAdmin;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.DTOs.PatienRegisterDto;
using Sehatak.Application.Interfaces.AuthPatient;
using Sehatak.Application.Interfaces.IEmail;
using Sehatak.Application.Interfaces.IProfileInterface;
using Sehatak.Domain.Entities;
using Sehatak.Domain.Entities.General;
using Sehatak.Domain.Entities.SharedEntities;
using Sehatak.Infrastructure.Data;
using Sehatak.Infrastructure.Security;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Infrastructure.Services.SuperAdminService.SuperAdminAuth
{
    public class SuperAdminProfileService : IProfile 
    {
        private readonly SharedDbContext sharedDbContext;
        private readonly IEmailService emailService;
        public SuperAdminProfileService(SharedDbContext sharedDbContext , IEmailService emailService)
        {
            this.sharedDbContext = sharedDbContext;
            this.emailService = emailService;
        }

        public async Task<NameResponse> EditName(int superAdminId, EditNameRequest request)
        {
            var superAdmin = await sharedDbContext.SuperAdmins.FindAsync(superAdminId);
            if (superAdmin == null)
                throw new BusinessException("Auth.Unauthorized");

            if (superAdmin.Name == request.Name)
                throw new BusinessException("General.NotFound");

            superAdmin.Name = request.Name;

            await sharedDbContext.SaveChangesAsync();

            return new NameResponse { Name = superAdmin.Name };

        }
        
        public async Task<ProfileImageResponse> EditProfileImage(int userId, EditProfileImageRequest request)
        {
            var superAdmin = await sharedDbContext.SuperAdmins.FindAsync(userId);
            if (superAdmin == null)
                throw new BusinessException("Auth.Unauthorized");

            if (request.ImageFile == null)
                throw new BusinessException("General.NotFound");

            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png" };
            var extension = Path.GetExtension(request.ImageFile.FileName).ToLower();
            if (!allowedExtensions.Contains(extension))
                throw new BusinessException("Validation.InvalidFileType");

            if (request.ImageFile.Length > 5 * 1024 * 1024)
                throw new BusinessException("Validation.FileTooLarge");

            var fileName = Guid.NewGuid() + Path.GetExtension(request.ImageFile.FileName);

            var path = Path.Combine("wwwroot/uploads/profileImage", fileName);

            using (var stream = new FileStream(path, FileMode.Create))
            {
                await request.ImageFile.CopyToAsync(stream);
            }

            superAdmin.ProfileImageUrl = $"/uploads/profileImage/{fileName}";

            
            await sharedDbContext.SaveChangesAsync();

            return new ProfileImageResponse
            {
                profileImage = superAdmin.ProfileImageUrl
            };

        }

        public async Task<bool> RequestEditEmail(int superAdminId, EditEmailRequest request)
        {
            var superAdmin = await sharedDbContext.SuperAdmins.FindAsync(superAdminId);
            if (superAdmin == null)
                throw new BusinessException("Auth.Unauthorized");

            var exists = await sharedDbContext.SuperAdmins
                .AnyAsync(x => x.Email == request.Email);

            if (exists)
                throw new BusinessException("Auth.EmailExists");

            var code = new Random().Next(100000, 999999).ToString();

            sharedDbContext.emailVerificationCodes.Add(new emailVerificationCode
            {
                SuperAdminId = superAdmin.Id,
                Code = code,
                Purpose = "change-email",
                PendingValue = request.Email,
                ExpiresAt = DateTime.UtcNow.AddMinutes(10)
            });

            await sharedDbContext.SaveChangesAsync();


            await emailService.SendOtpAsync(superAdmin.Email, code, "change-email");

            return true;
        }

        public async Task<EmailResponse> ConfirmEditEmail(int superAdminId, ConfirmEditEmailRequest request)
        {
            var superAdmin = await sharedDbContext.SuperAdmins.FindAsync(superAdminId);
            if (superAdmin == null)
                throw new BusinessException("Auth.Unauthorized");

            var validCode = await sharedDbContext.emailVerificationCodes
                .Where(c => c.SuperAdminId == superAdminId
                         && c.Purpose == "change-email"
                         && c.Code == request.Code
                         && !c.IsUsed
                         && c.ExpiresAt > DateTime.UtcNow)
                .OrderByDescending(c => c.CreatedAt)
                .FirstOrDefaultAsync();

            if (validCode == null || string.IsNullOrEmpty(validCode.PendingValue))
                throw new BusinessException("Verfiy.Code");

            superAdmin.Email = validCode.PendingValue;
            validCode.IsUsed = true;

            await sharedDbContext.SaveChangesAsync();

            return new EmailResponse { Email = superAdmin.Email };
        }

        public async Task<bool> RequestEditPassword(int superAdminId, EditPasswordRequest request)
        {
            var superAdmin = await sharedDbContext.SuperAdmins.FindAsync(superAdminId);
            if (superAdmin == null)
                throw new BusinessException("Auth.Unauthorized");

            if (request.PasswordHash != request.ConfirmPassword)
                throw new BusinessException("Validation.PasswordMismatch");

            var isSamePassword = BCrypt.Net.BCrypt.Verify(request.PasswordHash, superAdmin.PasswordHash);
            if (isSamePassword)
                throw new BusinessException("Validation.SamePassword");


            var code = new Random().Next(100000, 999999).ToString();

            sharedDbContext.emailVerificationCodes.Add(new emailVerificationCode
            {
                SuperAdminId = superAdminId,
                Code = code,
                Purpose = "change-password",
                PendingValue = BCrypt.Net.BCrypt.HashPassword(request.PasswordHash),
                ExpiresAt = DateTime.UtcNow.AddMinutes(10)
            });

            await sharedDbContext.SaveChangesAsync();

            await emailService.SendOtpAsync(superAdmin.Email, code, "change-password");

            return true;

        }

        public async Task<PasswordResponse> ConfirmEditPassword(int superAdminId, ConfirmEditPasswordRequest request)
        {
            var superAdmin = await sharedDbContext.SuperAdmins
                .FindAsync(superAdminId);

            if (superAdmin == null)
                throw new BusinessException("Auth.Unauthorized");

            var validCode = await sharedDbContext.emailVerificationCodes
                .Where(c => c.SuperAdminId == superAdminId
                       && c.Purpose == "change-password"
                       && !c.IsUsed
                       && c.Code == request.Code
                       && c.ExpiresAt > DateTime.UtcNow)
                .OrderByDescending(c => c.CreatedAt)
                .FirstOrDefaultAsync();
            if(validCode == null || string.IsNullOrEmpty(validCode.PendingValue))
                throw new BusinessException("Verfiy.Code");

            superAdmin.PasswordHash = validCode.PendingValue;
            validCode.IsUsed = true;

            await sharedDbContext.SaveChangesAsync();
            return new PasswordResponse { message = "Password Update Succses" };

        }

        public async Task<ProfileResponse> ViewProfile(int superAdminId)
        {
            var superAdmin = await sharedDbContext.SuperAdmins.FindAsync(superAdminId);
            if (superAdmin == null)
                throw new BusinessException("Auth.Unauthorized");

            string? Image = null;
            if (!string.IsNullOrEmpty(superAdmin.ProfileImageUrl))
            {
                Image = superAdmin.ProfileImageUrl;
            }

            return new ProfileResponse
            {
                Name = superAdmin.Name,
                Email = superAdmin.Email,
                Phone = superAdmin.phone,
                ProfileImage = Image
            };

        }
    }
}
