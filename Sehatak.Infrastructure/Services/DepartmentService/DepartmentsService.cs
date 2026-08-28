using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.DepartmentDto;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.Interfaces.DepartmentInterface;
using Sehatak.Domain.Entities.SharedEntities;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Infrastructure.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Infrastructure.Services.DepartmentService
{
    public class DepartmentsService : IDepartmentService
    {
        private readonly TenantDbContextFactory contextFactory;
        private readonly SharedDbContext sharedDbContext;
        public DepartmentsService(TenantDbContextFactory contextFactory, SharedDbContext sharedDbContext)
        {
            this.contextFactory = contextFactory;
            this.sharedDbContext = sharedDbContext;
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
                    departmentDescription=a.Description,
                    departmentName=a.Name,
                }).ToListAsync();

            return new GetDepartmentResponseDto
            {
                Departments = departments
            };
        }
    }
}
