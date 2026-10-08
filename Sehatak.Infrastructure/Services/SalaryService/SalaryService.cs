using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.DTOs.MonthlySalaryDto;
using Sehatak.Application.Interfaces.ISalary;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;

namespace Sehatak.Infrastructure.Services.SalaryService
{
    public class SalaryService : ISalary
    {
        private readonly SharedDbContext sharedDbContext;
        private readonly TenantDbContextFactory contextFactory;

        public SalaryService(SharedDbContext sharedDbContext, TenantDbContextFactory contextFactory)
        {
            this.sharedDbContext = sharedDbContext;
            this.contextFactory = contextFactory;
        }

        private async Task<TenantDbContext> OpenAdminDbAsync(int centerId, int userId)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            var db = contextFactory.CreateForCenter(centerId);

            var admin = await db.Users
                .FirstOrDefaultAsync(a => a.Id == userId
                                     && a.role == userRole.Admin
                                     && a.isActive);

            if (admin == null)
            {
                db.Dispose();
                throw new BusinessException("Auth.Forbidden");
            }

            return db;
        }

        private static void ValidateYearMonth(int year, int month)
        {
            if (year < 1 || year > 9999)
                throw new BusinessException("Validation.InvalidYear");

            if (month < 1 || month > 12)
                throw new BusinessException("Validation.InvalidMonth");
        }

        public async Task<CategorySalaryItemDto> SetCategorySalaryAsync(int centerId, int userId, SetCategorySalaryRequestDto request)
        {
            ValidateYearMonth(request.Year, request.Month);

            if (request.Amount < 0)
                throw new BusinessException("Validation.InvalidAmount");

            if (!Enum.IsDefined(request.Category))
                throw new BusinessException("Validation.InvalidCategory");

            using var db = await OpenAdminDbAsync(centerId, userId);

            var salary = await db.MonthlySalaries
                .FirstOrDefaultAsync(s => s.Year == request.Year
                                     && s.Month == request.Month
                                     && s.Category == request.Category);

            if (salary == null)
            {
                salary = new MonthlySalary
                {
                    Year = request.Year,
                    Month = request.Month,
                    Category = request.Category
                };
                await db.MonthlySalaries.AddAsync(salary);
            }

            salary.Amount = request.Amount;
            salary.UpdatedByUserId = userId;
            salary.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync();

            return new CategorySalaryItemDto
            {
                Category = salary.Category.ToString(),
                Amount = salary.Amount
            };
        }

        public async Task<DoctorSalaryItemDto> SetDoctorSalaryAsync(int centerId, int userId, SetDoctorSalaryRequestDto request)
        {
            ValidateYearMonth(request.Year, request.Month);

            if (request.Amount < 0)
                throw new BusinessException("Validation.InvalidAmount");

            using var db = await OpenAdminDbAsync(centerId, userId);

            var doctor = await db.Doctors
                .Include(d => d.user)
                .FirstOrDefaultAsync(d => d.Id == request.DoctorId
                                     && d.user.isActive);

            if (doctor == null)
                throw new BusinessException("Doctor.NotFound");

            var salary = await db.DoctorSalaries
                .FirstOrDefaultAsync(s => s.DoctorId == doctor.Id
                                     && s.Year == request.Year
                                     && s.Month == request.Month);

            if (salary == null)
            {
                salary = new DoctorSalary
                {
                    DoctorId = doctor.Id,
                    Year = request.Year,
                    Month = request.Month
                };
                await db.DoctorSalaries.AddAsync(salary);
            }

            salary.Amount = request.Amount;
            salary.UpdatedByUserId = userId;
            salary.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync();

            return new DoctorSalaryItemDto
            {
                DoctorId = doctor.Id,
                DoctorName = $"{doctor.user.firstName} {doctor.user.lastName}",
                Amount = salary.Amount
            };
        }

        public async Task<MonthlySalariesResponseDto> GetSalariesAsync(int centerId, int userId, int year, int month)
        {
            ValidateYearMonth(year, month);

            using var db = await OpenAdminDbAsync(centerId, userId);

            return await LoadMonthlySalariesAsync(db, year, month);
        }

        private static async Task<MonthlySalariesResponseDto> LoadMonthlySalariesAsync(TenantDbContext db, int year, int month)
        {
            var saved = await db.MonthlySalaries
                .Where(s => s.Year == year && s.Month == month)
                .ToListAsync();

            var categories = Enum.GetValues<SalaryCategory>()
                .Select(c => new CategorySalaryItemDto
                {
                    Category = c.ToString(),
                    Amount = saved.FirstOrDefault(s => s.Category == c)?.Amount ?? 0
                })
                .ToList();

            var doctors = await db.Doctors
                .Where(d => d.user.isActive
                       || db.DoctorSalaries.Any(s => s.DoctorId == d.Id
                                                && s.Year == year
                                                && s.Month == month))
                .OrderBy(d => d.user.firstName)
                .Select(d => new DoctorSalaryItemDto
                {
                    DoctorId = d.Id,
                    DoctorName = d.user.firstName + " " + d.user.lastName,
                    Amount = db.DoctorSalaries
                        .Where(s => s.DoctorId == d.Id
                               && s.Year == year
                               && s.Month == month)
                        .Select(s => s.Amount)
                        .FirstOrDefault()
                })
                .ToListAsync();

            var categoriesTotal = categories.Sum(c => c.Amount);
            var doctorsTotal = doctors.Sum(d => d.Amount);

            return new MonthlySalariesResponseDto
            {
                Year = year,
                Month = month,
                Categories = categories,
                Doctors = doctors,
                CategoriesTotal = categoriesTotal,
                DoctorsTotal = doctorsTotal,
                TotalSalaries = categoriesTotal + doctorsTotal
            };
        }
    }
}
