using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.EditProfile.EditEmailOrPasswored;
using Sehatak.Application.DTOs.EditProfile.EditProfileActors;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.Interfaces.IEmail;
using Sehatak.Application.Interfaces.IProfileInterface.ProfileAdmin;
using Sehatak.Domain.Entities.General;
using Sehatak.Domain.Entities.SharedEntities;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Infrastructure.Services.EditProfileService
{
    public class EditStaffService : IprofileStaff
    {
        private readonly SharedDbContext sharedDbContext;
        private readonly TenantDbContextFactory contextFactory;
        private readonly IEmailService emailService;

        public EditStaffService(SharedDbContext sharedDbContext ,  TenantDbContextFactory contextFactory , IEmailService emailService)
        {
            this.sharedDbContext = sharedDbContext;
            this.contextFactory = contextFactory;
            this.emailService = emailService;
        }

        public async Task<EditCenterInformationResponse> EditCenterInformation(int centerId, EditCenterInformationRequest request)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            if (request.PartialRefundPercent != null)
                center.PartialRefundPercent = (decimal)request.PartialRefundPercent;

            if (request.PrepaymentAmount != null)
                center.PrepaymentAmount = (decimal)request.PrepaymentAmount;

            if (request.RequiresPrepayment != null)
                center.RequiresPrepayment = (bool)request.RequiresPrepayment;

            if (request.RefundPolicyHours != null)
                center.RefundPolicyHours = (int)request.RefundPolicyHours;

            if (request.AdminWhatsappNumber != null)
                center.AdminWhatsappNumber = request.AdminWhatsappNumber;

            if(request.Phone!=null)
                center.Phone = request.Phone;

            if (request.Address != null)
                center.Address = request.Address;

            if (request.LogoUrl != null)
            {
                if (!string.IsNullOrEmpty(center.LogoUrl))
                    DeleteImageFile(center.LogoUrl);

                var fileName = Guid.NewGuid() + Path.GetExtension(request.LogoUrl.FileName);
                var path = Path.Combine("wwwroot/uploads/receipts", fileName);

                using (var stream = new FileStream(path, FileMode.Create))
                {
                    await request.LogoUrl.CopyToAsync(stream);
                }

                center.LogoUrl = $"/uploads/receipts/{fileName}";
            }
            else if (request.LogoUrl==null)
            {
                if (!string.IsNullOrEmpty(center.LogoUrl))
                    DeleteImageFile(center.LogoUrl);

                center.LogoUrl = null;
            }

            await sharedDbContext.SaveChangesAsync();

            return new EditCenterInformationResponse { 
            
                centerId=centerId,
                PartialRefundPercent = center.PartialRefundPercent,
                PrepaymentAmount = center.PrepaymentAmount,
                RefundPolicyHours = center.RefundPolicyHours,
                RequiresPrepayment = center.RequiresPrepayment,
                Address = center.Address,
                LogoUrl = center.LogoUrl,
                AdminWhatsappNumber = center.AdminWhatsappNumber,
                Phone = center.Phone

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

        public async Task<EditSttafInformationResponse> EditSttafInformation(int centerId , int userId, EditSttafInformationRequest request)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var user = await db.Users
            .FirstOrDefaultAsync(u => u.Id == userId
                             && u.isActive
                             && (u.role == userRole.Admin
                              || u.role == userRole.Receptionist
                              || u.role == userRole.LabTechnician 
                              || u.role == userRole.Doctor));
            if (user == null)
                throw new BusinessException("Auth.Forbidden");

            if (request.firstNmae != null)
                user.firstName = request.firstNmae;

            if (request.lastNmae != null)
                user.lastName = request.lastNmae;

            if (request.address != null)
                user.address = request.address;

            if (request.city != null)
                user.city = request.city;

            if(request.phoneNumber!=null)
                user.phoneNumber=request.phoneNumber;

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

            Doctor? doctor = null;
            if (user.role == userRole.Doctor)
            {
                doctor = await db.Doctors.FirstOrDefaultAsync(d => d.userId == userId);

                if (doctor == null)
                    throw new BusinessException("Doctor.NotFound");   

                if (request.Specialization != null)
                    doctor.Specialization = request.Specialization;

                if (request.Bio != null)
                    doctor.Bio = request.Bio;

                if(request.OnlineEnabled!=null )
                    doctor.OnlineEnabled = (bool)request.OnlineEnabled;
            }

            await db.SaveChangesAsync();

            return new EditSttafInformationResponse
            {
                StaffId = userId,
                FullName = user.firstName + " " + user.lastName,
                PhoneNumber = user.phoneNumber,
                ProfileImageUrl = user.ProfileImageUrl,
                Address = user.address,
                City = user.city,
                Specialization = doctor?.Specialization,   
                Bio = doctor?.Bio,
                Message = "تم تحديث البيانات بنجاح"
            };

        }

        public async Task<bool> RequestEditEmail(int centerId, int userId, EditEmailRequest request)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var user = await db.Users
            .FirstOrDefaultAsync(u => u.Id == userId
                             && u.isActive
                             && (u.role == userRole.Admin
                              || u.role == userRole.Receptionist
                              || u.role == userRole.LabTechnician
                              || u.role == userRole.Doctor));
            if (user == null)
                throw new BusinessException("Auth.Forbidden");


            var exists = await db.Users
                .AnyAsync(x => x.email == request.Email);

            if (exists)
                throw new BusinessException("Auth.EmailExists");

            var code = new Random().Next(100000, 999999).ToString();

            db.EmailVerificationCodes.Add(new EmailVerificationCode
            {
                UserId = user.Id,
                Code = code,
                Purpose = "change-email",
                PendingValue = request.Email,
                ExpiresAt = DateTime.UtcNow.AddMinutes(10)
            });

            await db.SaveChangesAsync();


            await emailService.SendOtpAsync(user.email, code, "change-email");

            return true;
        }

        public async Task<EmailResponse> ConfirmEditEmail(int centerId, int userId, ConfirmEditEmailRequest request)
        {
            var center = await sharedDbContext.MedicalCenters
            .FirstOrDefaultAsync(c => c.Id == centerId && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var user = await db.Users
            .FirstOrDefaultAsync(u => u.Id == userId
                             && u.isActive
                             && (u.role == userRole.Admin
                              || u.role == userRole.Receptionist
                              || u.role == userRole.LabTechnician
                              || u.role == userRole.Doctor));
            if (user == null)
                throw new BusinessException("Auth.Forbidden");

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

        public async Task<bool> RequestEditPassword(int centerId, int userId, EditPasswordRequest request)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var user = await db.Users
                .FirstOrDefaultAsync(u => u.Id == userId
                                 && u.isActive
                                 && (u.role == userRole.Admin
                                  || u.role == userRole.Receptionist
                                  || u.role == userRole.LabTechnician
                                  || u.role == userRole.Doctor));
            if (user == null)
                throw new BusinessException("Auth.Forbidden");

            if (request.PasswordHash != request.ConfirmPassword)
                throw new BusinessException("Validation.PasswordMismatch");

            var isSamePassword = BCrypt.Net.BCrypt.Verify(request.PasswordHash, user.passwordHash);
            if (isSamePassword)
                throw new BusinessException("Validation.SamePassword");

            var code = new Random().Next(100000, 999999).ToString();

            db.EmailVerificationCodes.Add(new EmailVerificationCode
            {
                UserId = user.Id,
                Code = code,
                Purpose = "change-password",  
                PendingValue = request.PasswordHash,
                ExpiresAt = DateTime.UtcNow.AddMinutes(10)
            });

            await db.SaveChangesAsync();

            await emailService.SendOtpAsync(user.email, code, "change-password");   

            return true;
        }

        public async Task<PasswordResponse> ConfirmEditPassword(int centerId, int userId, ConfirmEditPasswordRequest request)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var user = await db.Users
                .FirstOrDefaultAsync(u => u.Id == userId
                                 && u.isActive
                                 && (u.role == userRole.Admin
                                  || u.role == userRole.Receptionist
                                  || u.role == userRole.LabTechnician
                                  || u.role == userRole.Doctor));
            if (user == null)
                throw new BusinessException("Auth.Forbidden");

            var validCode = await db.EmailVerificationCodes
                .Where(c => c.UserId == userId
                         && c.Purpose == "change-password"   
                         && c.Code == request.Code
                         && !c.IsUsed
                         && c.ExpiresAt > DateTime.UtcNow)
                .OrderByDescending(c => c.CreatedAt)
                .FirstOrDefaultAsync();

            if (validCode == null || string.IsNullOrEmpty(validCode.PendingValue))
                throw new BusinessException("Verfiy.Code");

            user.passwordHash = BCrypt.Net.BCrypt.HashPassword(validCode.PendingValue);   
            validCode.IsUsed = true;

            await db.SaveChangesAsync();

            return new PasswordResponse { message = "Password Update Succses" };
        }
    }
}
