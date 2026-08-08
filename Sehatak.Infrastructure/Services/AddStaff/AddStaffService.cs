using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.EntityFrameworkCore;
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
    public class AddStaffService : ISignup
    {
        private readonly SharedDbContext sharedDbContext;
        private readonly TenantDbContextFactory contextFactory;
        private readonly IEmailService emailService;
        public AddStaffService(SharedDbContext sharedDbContext , TenantDbContextFactory contextFactory , IEmailService emailService)
        {
            this.sharedDbContext = sharedDbContext;
            this.contextFactory = contextFactory;
            this.emailService = emailService;
        }

        public async Task<LabTechnicianResponseDto> AddLabTechnicianAsync(int centerId, LabTechnicianRequestDto request)
        {
            var center = await sharedDbContext.MedicalCenters
               .FirstOrDefaultAsync(c => c.Id == centerId && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var LabTechnician = await db.Users
                .FirstOrDefaultAsync(e => e.email == request.email);

            if (LabTechnician != null)
                throw new BusinessException("Auth.EmailExists");
            var tempPaswored = GenerateTempPassword();

            var newLabTechnician = new User
            {
                firstName = request.LapTechnicalFirstName,
                lastName = request.LapTechnicalLastName,
                email = request.email,
                passwordHash = BCrypt.Net.BCrypt.HashPassword(tempPaswored),
                isActive = true,
                role = userRole.LabTechnician,
                address = request.address,
                createdAt = DateTime.UtcNow,
                city = request.city,
            };
            if (request.phoneNumber != null)
            {
                newLabTechnician.phoneNumber = request.phoneNumber;
            }
            if (request.ProfileImage != null)
            {
                var fileName = Guid.NewGuid() + Path.GetExtension(request.ProfileImage.FileName);
                var path = Path.Combine("wwwroot/uploads/profileImage", fileName);
                using (var stream = new FileStream(path, FileMode.Create))
                {
                    await request.ProfileImage.CopyToAsync(stream);
                }
                newLabTechnician.ProfileImageUrl = $"/uploads/profileImage/{fileName}";
            }
            await db.Users.AddAsync(newLabTechnician);
            await db.SaveChangesAsync();
            await emailService.SendTempPasswordAsync(
                request.email,
                 name: $"{request.LapTechnicalFirstName} {request.LapTechnicalLastName}",
                 tempPaswored,
                  center.Name);

            return new LabTechnicianResponseDto
            {
                UserId = newLabTechnician.Id,
                Email = newLabTechnician.email,
                Message = "تم التسجيل، يرجى الانتباه لكلمة المرور وتغيريها في أقرب وقت."
            };
        
        }

        public async Task<ReceptionistResponseDto> AddReceptionistAsync(int centerId, ReceptionistRequestDto request)
        {
            var center = await sharedDbContext.MedicalCenters
               .FirstOrDefaultAsync(c => c.Id == centerId && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var Receptionist = await db.Users
                .FirstOrDefaultAsync(e => e.email == request.email);

            if (Receptionist != null)
                throw new BusinessException("Auth.EmailExists");
            var tempPaswored = GenerateTempPassword();

            var newReceptionist = new User
            {
                firstName = request.ReceptionistFirstName,
                lastName = request.ReceptionistLastName,
                email = request.email,
                passwordHash = BCrypt.Net.BCrypt.HashPassword(tempPaswored),
                isActive = true,
                role = userRole.Receptionist,
                address = request.address,
                createdAt = DateTime.UtcNow,
                city = request.city,
            };
            if (request.phoneNumber != null)
            {
                newReceptionist.phoneNumber = request.phoneNumber;
            }
            if (request.ProfileImage != null)
            {
                var fileName = Guid.NewGuid() + Path.GetExtension(request.ProfileImage.FileName);
                var path = Path.Combine("wwwroot/uploads/profileImage", fileName);
                using (var stream = new FileStream(path, FileMode.Create))
                {
                    await request.ProfileImage.CopyToAsync(stream);
                }
                newReceptionist.ProfileImageUrl = $"/uploads/profileImage/{fileName}";
            }
            await db.Users.AddAsync(newReceptionist);
            await db.SaveChangesAsync();

            await emailService.SendTempPasswordAsync(
                 request.email,
                 name: $"{request.ReceptionistFirstName} {request.ReceptionistLastName}",
                 tempPaswored,
                 center.Name);

            return new ReceptionistResponseDto { 
              UserId = newReceptionist.Id,
              Email = newReceptionist.email,
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
    }
}
