using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.EditProfile.EditEmailOrPasswored;
using Sehatak.Application.DTOs.EditProfile.EditProfileActors;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.Interfaces.IEmail;
using Sehatak.Application.Interfaces.IProfileInterface;
using Sehatak.Domain.Entities.General;
using Sehatak.Domain.Entities.SharedEntities;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Umbraco.Core.Security;
namespace Sehatak.Infrastructure.Services.PatientService.PatientProfile
{
    public class PatientProfileService : IProfilePatient
    {
        private readonly SharedDbContext sharedDbContext;
        private readonly TenantDbContextFactory contextFactory;
        private readonly IEmailService emailService;
        public PatientProfileService(SharedDbContext sharedDbContext, TenantDbContextFactory contextFactory , IEmailService emailService)
        {
            this.sharedDbContext = sharedDbContext;
            this.contextFactory = contextFactory;
            this.emailService = emailService;
        }
        public async Task<EditPatientInformationResponse> EditPatientInformation(int centerId , int userId, EditPatientInformationRequest request)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId 
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var user = await db.Users
                .FirstOrDefaultAsync(p => p.Id == userId
                                     && p.isActive);

            if (user == null)
                throw new BusinessException("Patient.NotFound");


            if (request.firstNmae != null)
                user.firstName = request.firstNmae;

            if (request.lastNmae != null)
                user.lastName = request.lastNmae;

            if (request.address != null)
                user.address = request.address;

            if (request.city != null)
                user.city = request.city;

            if (request.phoneNumber != null)
                user.phoneNumber = request.phoneNumber;

            if (request.profileImage != null)
            {
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png" };
                var extension = Path.GetExtension(request.profileImage.FileName).ToLower();
                if (!allowedExtensions.Contains(extension))
                    throw new BusinessException("Validation.InvalidFileType");

                if (request.profileImage.Length > 5 * 1024 * 1024)
                    throw new BusinessException("Validation.FileTooLarge");

                if (!string.IsNullOrEmpty(user.ProfileImageUrl))
                    DeleteImageFile(user.ProfileImageUrl);

                var fileName = Guid.NewGuid() + Path.GetExtension(request.profileImage.FileName);
                var path = Path.Combine("wwwroot/uploads/profileImage", fileName);

                using (var stream = new FileStream(path, FileMode.Create))
                {
                    await request.profileImage.CopyToAsync(stream);
                }

                user.ProfileImageUrl = $"/uploads/profileImage/{fileName}";
            }
            else if (request.RemoveProfileImage)
            {
                if (!string.IsNullOrEmpty(user.ProfileImageUrl))
                    DeleteImageFile(user.ProfileImageUrl);

                user.ProfileImageUrl = null;
            }

            await db.SaveChangesAsync();
            return new EditPatientInformationResponse
            {
                FullName = $"{user.firstName} {user.lastName}",
                ProfileImageUrl = user.ProfileImageUrl,
                Address = user.address,
                City = user.city,
                PhoneNumber = user.phoneNumber
            };

        }

        private void DeleteImageFile(string relativeUrl)
        {
            try
            {
                var webRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                var fullPath = Path.Combine(webRoot, relativeUrl.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString()));

                if (File.Exists(fullPath))
                    File.Delete(fullPath);
            }
            catch (Exception)
            {

            }
        }

        public async Task<EditPatientInformationResponse> ViewPatientInformation(int centerId , int userId)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId 
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var user = await db.Users
                .FirstOrDefaultAsync(p => p.Id == userId
                                     && p.isActive);

            if (user == null)
                throw new BusinessException("Patient.NotFound");

            string? Image = null;
            if (!string.IsNullOrEmpty(user.ProfileImageUrl))
            {
                Image = user.ProfileImageUrl;
            }

            return new EditPatientInformationResponse
            {   FullName = $"{user.firstName} {user.lastName}",
                ProfileImageUrl = Image,
                Address = user.address,
                City = user.city,
                PhoneNumber = user.phoneNumber
            };
        }

        public async Task<bool> RequestEditEmail(int centerId , int userId, EditEmailRequest request)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId 
                                     && c.CenterStatus == CenterStatus.Active); 

            if(center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);
            var user = await db.Users
                .FirstOrDefaultAsync(p => p.Id == userId
                                     && p.isActive);

            if(user == null)
                throw new BusinessException("Patient.NotFound");

            var exist = await db.Users
                .AnyAsync(u => u.email == request.Email);

            if(exist)
                throw new BusinessException("Email.AlreadyExists");

            var code = new Random().Next(100000, 999999).ToString();
            db.EmailVerificationCodes.Add(new EmailVerificationCode
            {
                UserId = userId,
                Code = code,
                Purpose = "change-email",
                PendingValue = request.Email,
                ExpiresAt = DateTime.UtcNow.AddMinutes(10)
            });
            await db.SaveChangesAsync();
            await emailService.SendOtpAsync(user.email, code, "change-email");
            return true;

        }

        public async Task<EmailResponse> ConfirmEditEmail(int centerId , int userId, ConfirmEditEmailRequest request)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);
            var user = await db.Users
                .FirstOrDefaultAsync(p => p.Id == userId
                                     && p.isActive);

            if (user == null)
                throw new BusinessException("Patient.NotFound");

            var validCode = await db.EmailVerificationCodes
               .Where(c => c.UserId == userId
                        && c.Purpose == "change-email"
                        && c.Code == request.Code
                        && !c.IsUsed
                        && c.ExpiresAt > DateTime.UtcNow)
               .OrderByDescending(c => c.CreatedAt)
               .FirstOrDefaultAsync();

            if (validCode == null || string.IsNullOrEmpty(validCode.PendingValue))
                throw new BusinessException("Verfiy.Code");

            user.email = validCode.PendingValue;
            validCode.IsUsed = true;

            await db.SaveChangesAsync();

            return new EmailResponse { Email = user.email };
        }

        public async Task<bool> RequestEditPassword(int centerId , int userId, EditPasswordRequest request)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var user = await db.Users
                .FirstOrDefaultAsync(p => p.Id == userId
                                     && p.isActive);

            if(user == null)
                throw new BusinessException("Patient.NotFound");

            if (request.PasswordHash != request.ConfirmPassword)
                throw new BusinessException("Validation.PasswordMismatch");

            var isSamePassword = BCrypt.Net.BCrypt.Verify(request.PasswordHash, user.passwordHash);
            if (isSamePassword)
                throw new BusinessException("Validation.SamePassword");


            var code = new Random().Next(100000, 999999).ToString();

            db.EmailVerificationCodes.Add(new EmailVerificationCode
            {
                UserId = userId,
                Code = code,
                Purpose = "change-password",
                PendingValue = BCrypt.Net.BCrypt.HashPassword(request.PasswordHash),
                ExpiresAt = DateTime.UtcNow.AddMinutes(10)
            });

            await db.SaveChangesAsync();

            await emailService.SendOtpAsync(user.email, code, "change-password");

            return true;
        }

        public async Task<PasswordResponse> ConfirmEditPassword(int centerId , int userId, ConfirmEditPasswordRequest request)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var user = await db.Users
                .FirstOrDefaultAsync(p => p.Id == userId
                                     && p.isActive);

            if (user == null)
                throw new BusinessException("Patient.NotFound");
            var validCode = await db.EmailVerificationCodes
                .Where(c => c.UserId == userId
                       && c.Purpose == "change-password"
                       && !c.IsUsed
                       && c.Code == request.Code
                       && c.ExpiresAt > DateTime.UtcNow)
                .OrderByDescending(c => c.CreatedAt)
                .FirstOrDefaultAsync();
            if (validCode == null || string.IsNullOrEmpty(validCode.PendingValue))
                throw new BusinessException("Verfiy.Code");

            user.passwordHash = validCode.PendingValue;
            validCode.IsUsed = true;

            await db.SaveChangesAsync();
            return new PasswordResponse { message = "Password Update Succses" };
        }
    }
}
