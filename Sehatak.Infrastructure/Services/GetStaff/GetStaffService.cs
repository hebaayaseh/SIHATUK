using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.DTOs.GetStaffDto;
using Sehatak.Application.Interfaces.GetSttafInterFace;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;

namespace Sehatak.Infrastructure.Services.GetStaff
{
    public class GetStaffService : IGetStaff
    {
        private readonly SharedDbContext SharedDbContext;
        private readonly TenantDbContextFactory contextFactory;
        public GetStaffService(TenantDbContextFactory contextFactory,SharedDbContext sharedDbContext)
        {
            this.SharedDbContext = sharedDbContext;
            this.contextFactory = contextFactory;
        }

        public async Task<DoctorSummaryDto?> GetDoctorAsync(int centerId, int doctorId , int? year = null, int? month = null)
{
            var center = await SharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId && c.CenterStatus == CenterStatus.Active);
            if (center == null)
                throw new BusinessException("Center.NotFound");


            using var db = contextFactory.CreateForCenter(centerId);

            var doctor = await db.Doctors
                .Include(d => d.user)
                .Include(d => d.doctorschedules)
                .FirstOrDefaultAsync(d => d.Id == doctorId && d.user.isActive);

            if (doctor == null)
                throw new BusinessException("Doctor.NotFound");

            var targetYear = year ?? DateTime.UtcNow.Year;
            var targetMonth = month ?? DateTime.UtcNow.Month;

            var startOfMonth = new DateOnly(targetYear, targetMonth, 1);
            var endOfMonth = startOfMonth.AddMonths(1).AddDays(-1);


            var blockedDates = await db.DoctorBlockedDays
                .Where(d => d.doctorId == doctor.Id
                       && d.isBlocked
                       && d.date >= startOfMonth
                       && d.date <= endOfMonth)
                .Select(d => d.date)
                .ToListAsync();

            return new DoctorSummaryDto
            {
                DoctorId = doctor.Id,
                DoctorName = $"{doctor.user.firstName} {doctor.user.lastName}",
                OnlineEnabled = doctor.OnlineEnabled,
                Bio = doctor.Bio,
                Specialization = doctor.Specialization,
                ProfileImageUrl = doctor.user.ProfileImageUrl,
                email = doctor.user.email,
                phoneNumber=doctor.user.phoneNumber,
                BlockedDates = blockedDates.Cast<DateOnly?>().ToList(),
                doctorSchedule = doctor.doctorschedules
                    .Where(s => s.IsActive)
                    .Select(d => new SummarySchedualDto
                    {
                        Id = d.Id,
                        StartTime = d.StartTime,
                        EndTime = d.EndTime,
                        SlotDurationMinutes = d.SlotDurationMinutes,
                        IsActive = d.IsActive,
                        DayOfWeek = d.DayOfWeek
                    }).ToList(),
            };
}

        public async Task<List<GetDoctorsResponseDto>> GetDoctorsAsync(int centerId)
        {
            var center = await SharedDbContext.MedicalCenters
                 .FirstOrDefaultAsync(c => c.Id == centerId && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            return await db.Departments
                .Select(p => new GetDoctorsResponseDto
                {
                    DepartmentId = p.Id,
                    DepartmentName = p.Name,
                    DepartmentDescription = p.Description,
                    DepartmentImageUrl = p.ImageUrl,
                    Doctors = p.Doctors
                    .Select(a=>new DoctorsSummaryDto
                    {
                        DoctorId = a.Id,
                        DoctorName = a.user.firstName+" "+a.user.lastName,
                        isActive = a.user.isActive,
                        
                    }).ToList()
                }).ToListAsync();

        }

        
    }
}
