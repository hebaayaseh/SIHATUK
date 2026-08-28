using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.DTOs.GetStaffDto;
using Sehatak.Application.Interfaces.GetSttafInterFace;
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

        public async Task<DoctorSummaryDto?> GetDoctorAsync(int centerId, int doctorId)
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

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var blockedDates = await db.DoctorBlockedDays
                        .Where(d => d.doctorId == doctor.Id 
                               && d.isBlocked 
                               && d.date >= today)
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
                BlockedDates = blockedDates.Cast<DateOnly?>().ToList(),
                doctorSchedule = doctor.doctorschedules
                    .Where(s => s.IsActive)
                    .Select(d => new SummatySchedualDto
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
                .Where(p => p.Doctors.Any(a => a.user.isActive))
                .Select(p => new GetDoctorsResponseDto
                {
                    DepartmentId = p.Id,
                    DepartmentName = p.Name,
                    DepartmentDescription = p.Description,
                    DepartmentImageUrl = p.ImageUrl,
                    Doctors = p.Doctors
                    .Where(a=>a.user.isActive)
                    .Select(a=>new DoctorSummaryDto
                    {
                        DoctorId = a.Id,
                        DoctorName = a.user.firstName+" "+a.user.lastName,
                        Specialization = a.Specialization,
                        ProfileImageUrl = a.user.ProfileImageUrl,
                        OnlineEnabled = a.OnlineEnabled,
                        Bio = a.Bio,
                        
                    } ).ToList()
                }).ToListAsync();

        }

        public async Task<GetStaffResponseDto> GetStaffAsync(int centerId, int userId)
        {
            var center = await SharedDbContext.MedicalCenters
              .FirstOrDefaultAsync(c => c.Id == centerId 
                                   && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var user = await db.Users
               .FirstOrDefaultAsync(u => u.Id == userId 
                                    && u.isActive 
                                    && (u.role == userRole.LabTechnician
                                    || u.role == userRole.GeneralDoctor
                                    || u.role == userRole.Nurse
                                    || u.role == userRole.Receptionist));

            if (user == null)
                throw new BusinessException("User.NotFound");


            var shift = await db.StaffShifts
              .Where(a => a.UserId == userId)
              .ToListAsync();

            var attendence = await db.StaffAttendances
                .Where(a => a.UserId == userId)
                .ToListAsync();

            return new GetStaffResponseDto
            {
                UserId = user.Id,
                UserName = $"{user.firstName} {user.lastName}",
                PhoneNumber = user.phoneNumber,
                Address = user.address,
                Email = user.email,
                IsActive = user.isActive,
                Role = user.role.ToString(),
                StaffShift = shift.Select(a => new SummaryShiftDto
                {
                    ShiftName = a.ShiftName,
                    ShistDate = a.ShiftDate,
                }).ToList()

            };
        }

        public async Task<List<GetStaffResponseDto>> GetStaffsAsync(int centerId)
        {
            var center = await SharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId 
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);
            

            return await db.Users
                .Where(u => (u.role == userRole.LabTechnician
                             || u.role == userRole.GeneralDoctor
                             || u.role == userRole.Nurse
                             || u.role == userRole.Receptionist)
                             && u.isActive)
                .Select(r => new GetStaffResponseDto
                {
                    UserId = r.Id,
                    UserName = r.firstName + " " + r.lastName
                    
                }).ToListAsync();
        }

        
    }
}
