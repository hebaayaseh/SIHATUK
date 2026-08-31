using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.DepartmentDto;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.DTOs.GetStaffDto;
using Sehatak.Application.DTOs.StaffSignup;
using Sehatak.Application.Interfaces.IEmail;
using Sehatak.Application.Interfaces.SignUp;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Infrastructure.Services.AddStaff
{
    public class StaffService : ISignup
    {
        private readonly SharedDbContext sharedDbContext;
        private readonly TenantDbContextFactory contextFactory;
        private readonly IEmailService emailService;
        public StaffService(SharedDbContext sharedDbContext , TenantDbContextFactory contextFactory , IEmailService emailService)
        {
            this.sharedDbContext = sharedDbContext;
            this.contextFactory = contextFactory;
            this.emailService = emailService;
        }

        

        public async Task<AddStafResponseDto> AddStafAsync(int userId, int centerId, AddStaffRequestDto request)
        {
            var center = await sharedDbContext.MedicalCenters
               .FirstOrDefaultAsync(c => c.Id == centerId && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            if (request.userRole == userRole.SuperAdmin || request.userRole == userRole.Patient || request.userRole == userRole.Doctor)
                throw new BusinessException("Auth.Forbidden");

            var admin = await db.Users
                .FirstOrDefaultAsync(a => a.Id == userId
                                     && a.isActive);
            if (admin == null)
                throw new BusinessException("Auth.Forbidden");


            var Staff = await db.Users
                .FirstOrDefaultAsync(e => e.email == request.email);

            if (Staff != null)
                throw new BusinessException("Auth.EmailExists");
            var tempPaswored = GenerateTempPassword();

            var newStaff = new User
            {
                firstName = request.FirstName,
                lastName = request.LastName,
                email = request.email,
                passwordHash = BCrypt.Net.BCrypt.HashPassword(tempPaswored),
                isActive = true,
                role = request.userRole,
                address = request.address,
                createdAt = DateTime.UtcNow,
                city = request.city,
            };
            if (request.phoneNumber != null)
            {
                newStaff.phoneNumber = request.phoneNumber;
            }
            if (request.ProfileImage != null)
            {
                var fileName = Guid.NewGuid() + Path.GetExtension(request.ProfileImage.FileName);
                var path = Path.Combine("wwwroot/uploads/profileImage", fileName);
                using (var stream = new FileStream(path, FileMode.Create))
                {
                    await request.ProfileImage.CopyToAsync(stream);
                }
                newStaff.ProfileImageUrl = $"/uploads/profileImage/{fileName}";
            }
            await db.Users.AddAsync(newStaff);
            await db.SaveChangesAsync();
            await emailService.SendTempPasswordAsync(
                request.email,
                 name: $"{request.FirstName} {request.LastName}",
                 tempPaswored,
                  center.Name);

            return new AddStafResponseDto
            {
                UserId = newStaff.Id,
                Email = newStaff.email,
                Message = "تم التسجيل، يرجى الانتباه لكلمة المرور وتغيريها في أقرب وقت."
            };
        }

        private string GenerateTempPassword()
        {
            const string chars = "ABCDEFGHJKMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789";
            var random = new Random();
            return new string(Enumerable.Repeat(chars, 10)
                .Select(s => s[random.Next(s.Length)])
                .ToArray());
        }
        public async Task<bool> ActiveStaffAsync(int centerId, RemoveStaffRequestDto request)
        {
            var center = await sharedDbContext.MedicalCenters
              .FindAsync(centerId);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);
            var user = await db.Users
                .FirstOrDefaultAsync(u => u.Id == request.userId);

            if (user == null)
                throw new BusinessException("General.NotFound");
            if (user.role == userRole.Patient)
                throw new BusinessException("Auth.Forbidden");

            user.isActive = true;
            await db.SaveChangesAsync();

            return true;
        }

        public async Task<bool> RemoveStaffAsync(int centerId, RemoveStaffRequestDto request)
        {
            var center = await sharedDbContext.MedicalCenters
                .FindAsync(centerId);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);
            var user = await db.Users
                .FirstOrDefaultAsync(u => u.Id == request.userId);

            if (user == null)
                throw new BusinessException("General.NotFound");
            if (user.role == userRole.Patient)
                throw new BusinessException("Auth.Forbidden");

            user.isActive = false;
            await db.SaveChangesAsync();

            return true;
        }
    }
}
