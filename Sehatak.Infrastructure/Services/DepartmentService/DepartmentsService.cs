using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.DepartmentDto;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.DTOs.StaffSignup;
using Sehatak.Application.Interfaces.DepartmentInterface;
using Sehatak.Application.Interfaces.IEmail;
using Sehatak.Domain.Entities.SharedEntities;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.Infrastructure.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Umbraco.Core.Security;

namespace Sehatak.Infrastructure.Services.DepartmentService
{
    public class DepartmentsService : IDepartmentService
    {
        private readonly TenantDbContextFactory contextFactory;
        private readonly SharedDbContext sharedDbContext;
        private IEmailService emailService;
        public DepartmentsService(TenantDbContextFactory contextFactory, SharedDbContext sharedDbContext, IEmailService emailService)
        {
            this.contextFactory = contextFactory;
            this.sharedDbContext = sharedDbContext;
            this.emailService = emailService;
        }

        public async Task<DepartmentResponseDto> AddDepartmentAsync(int centerId,DepartmentRequestDto request)
        {
            var center = await sharedDbContext.MedicalCenters
                .FindAsync(centerId);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var department = await db.Departments
                .FirstOrDefaultAsync(d => d.Name == request.departmentName);

            if (department != null)
                throw new BusinessException("Department.Exist");
            string? imageUrl = null;
            if (request.logo != null)
            {
                var fileName = Guid.NewGuid() + Path.GetExtension(request.logo.FileName);

                var path = Path.Combine("wwwroot/uploads/receipts", fileName);

                using (var stream = new FileStream(path, FileMode.Create))
                {
                    await request.logo.CopyToAsync(stream);
                }

                imageUrl = $"/uploads/receipts/{fileName}";
            }
            var newDepartment = new Department
            { 
                Name = request.departmentName,
                Description = request.departmentDescription,
                ImageUrl = imageUrl
            };
            await db.Departments.AddAsync(newDepartment);
            await db.SaveChangesAsync();

            return new DepartmentResponseDto
            {
                departmentId = newDepartment.Id,
                departmentName = newDepartment.Name,
                logo = newDepartment.ImageUrl
            };
        }

        public async Task<DepartmentResponseDto> UpdateDepartmentAsync(int centerId, DepartmentUpdateRequestDto request)
        {
            var center = await sharedDbContext.MedicalCenters
               .FindAsync(centerId);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var department = await db.Departments
                .FirstOrDefaultAsync(d => d.Id== request.departmentId);

            if (department == null)
                throw new BusinessException("Department.NotFound");

            if (request.departmentName != null) department.Name = request.departmentName;
            if (request.departmentdiscription != null) department.Description = request.departmentdiscription;
            if (request.logo!=null)
            {
                var fileName = Guid.NewGuid() + Path.GetExtension(request.logo.FileName);

                var path = Path.Combine("wwwroot/uploads/receipts", fileName);

                using (var stream = new FileStream(path, FileMode.Create))
                {
                    await request.logo.CopyToAsync(stream);
                }

                department.ImageUrl = $"/uploads/receipts/{fileName}";
            }
            await db.SaveChangesAsync();
            return new DepartmentResponseDto { 
                 departmentId = department.Id,
                 departmentName = department.Name,
                 logo = department.ImageUrl

            };

        }
        public async Task<string> RemoveDepartmentAsync(int centerId, DepartmentRemoveRequestDto request)
        {
            var center = await sharedDbContext.MedicalCenters
                .FindAsync(centerId);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);
            var department = await db.Departments.FirstOrDefaultAsync(d => d.Id == request.departmentId);

            if (department == null)
                throw new BusinessException("Department.NotFound");

            db.Departments.Remove(department);
            await db.SaveChangesAsync();

            return "Department.Removed";
        }

        public async Task<GetDepartmentResponseDto> GetDepartmentsAsync(int centerId)
        {
            var center = await sharedDbContext.MedicalCenters
                .FindAsync(centerId);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var departments = await db.Departments
                .Select(a => new DepartmentSummaryDto
                {
                    Id = a.Id,
                    departmentDescription = a.Description,
                    departmentName = a.Name,
                }).ToListAsync();

            return new GetDepartmentResponseDto
            {
                Departments = departments
            };
        }
    
    public async Task<DoctorResponseDto> RegisterDoctorAsync(int centerId, DoctorRequestDto request)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var department = await db.Departments
                .FirstOrDefaultAsync(d => d.Id == request.departmentId);

            if (department == null)
                throw new BusinessException("Department.NotFound");

            var emailExists = await db.Users.AnyAsync(u => u.email == request.email);
            if (emailExists)
                throw new BusinessException("Auth.EmailExists");


            var tempPasswored = GenerateTempPassword();
            var user = new User
            {
                firstName = request.doctorFirstName,
                lastName = request.doctorLastName,
                address = request.address,
                createdAt = DateTime.UtcNow,
                email = request.email,
                city = request.city,
                role = userRole.Doctor,
                passwordHash = BCrypt.Net.BCrypt.HashPassword(tempPasswored),
                isActive = true
            };
            if (request.phoneNumber != null)
            {
                user.phoneNumber = request.phoneNumber;
            }
            if (request.ProfileImage != null)
            {
                var fileName = Guid.NewGuid() + Path.GetExtension(request.ProfileImage.FileName);
                var path = Path.Combine("wwwroot/uploads/receipts", fileName);
                using (var stream = new FileStream(path, FileMode.Create))
                {
                    await request.ProfileImage.CopyToAsync(stream);
                }
                user.ProfileImageUrl = $"/uploads/receipts/{fileName}";
            }
            await db.Users.AddAsync(user);
            await db.SaveChangesAsync();

            var doctor = new Doctor
            {
                userId = user.Id,
                Specialization = request.Specialization,
                Bio = request.Bio,
                departmentId = request.departmentId,
                OnlineEnabled = request.OnlineEnabled,
            };

            await db.Doctors.AddAsync(doctor);
            await db.SaveChangesAsync();

            await emailService.SendTempPasswordAsync(
                request.email,
                name: $"{request.doctorFirstName} {request.doctorLastName}",
                tempPasswored, center.Name);

            return new DoctorResponseDto
            {
                UserId = user.Id,
                Email = request.email,
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
