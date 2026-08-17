using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.DTOs.GetStaffDto;
using Sehatak.Application.Interfaces.GetSttafInterFace;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
                        .Where(d => d.doctorId == doctor.Id && d.isBlocked && d.date >= today)
                        .FirstOrDefaultAsync();


            return new DoctorSummaryDto
            {
                DoctorId = doctor.Id,
                DoctorName = $"{doctor.user.firstName} {doctor.user.lastName}",
                OnlineEnabled = doctor.OnlineEnabled,
                Bio = doctor.Bio,
                Specialization = doctor.Specialization,
                ProfileImageUrl = doctor.user.ProfileImageUrl,
                BlockedDates = blockedDates!=null? blockedDates.date : null,
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

        public async Task<GetLapTechnicalDto> GetLapTechnicalAsync(int centerId, int userId)
        {
            var center = await SharedDbContext.MedicalCenters
              .FirstOrDefaultAsync(c => c.Id == centerId && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var user = await db.Users
               .FirstOrDefaultAsync(u => u.Id == userId && u.isActive && u.role == userRole.LabTechnician);
            if (user == null)
                throw new BusinessException("User.NotFound");


            var attendances = await db.StaffAttendances
              .Include(a => a.Shift)
              .Where(a => a.UserId == userId)
              .OrderByDescending(a => a.AttendanceDate)
              .ToListAsync();

            if (attendances == null)
            {
                throw new BusinessException("User.NotFound");
            }

            return new GetLapTechnicalDto
            {
                LapTechnicalId = user.Id,
                LapTechnicalName = $"{user.firstName} {user.lastName}",
                LabTechnicalShift = attendances.Select(a => new SummaryShiftDto
                {
                    ShiftName = a.Shift.ShiftName,
                    ShistDate = a.Shift.ShiftDate,
                    CheckInTime = a.CheckInTime,
                    CheckOutTime = a.CheckOutTime,
                    attendanceStatus = a.attendanceStatus,
                    IsActive = user.isActive,
                    AttendanceDate = a.AttendanceDate,
                }).ToList()

            };
        }

        public async Task<List<GetLapTechnicalDto>> GetLapTechnicalsAsync(int centerId)
        {
            var center = await SharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            return await db.Users
                .Where(u => u.role == userRole.LabTechnician && u.isActive)
                .Select(r => new GetLapTechnicalDto
                {

                    LapTechnicalId = r.Id,
                    LapTechnicalName = r.firstName + " " + r.lastName
                    
                }).ToListAsync();
        }

        public async Task<GetReceptionistResponseDto> GetReceptionistAsync(int centerId, int userId)
        {
            var center = await SharedDbContext.MedicalCenters
              .FirstOrDefaultAsync(c => c.Id == centerId && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var user = await db.Users
               .FirstOrDefaultAsync(u => u.Id == userId && u.isActive && u.role == userRole.Receptionist);
            if (user == null)
                throw new BusinessException("User.NotFound");


            var attendances = await db.StaffAttendances
              .Include(a => a.Shift)
              .Where(a => a.UserId == userId)
              .OrderByDescending(a => a.AttendanceDate)
              .ToListAsync();

            if (attendances == null)
            {
                throw new BusinessException("User.NotFound");
            }

            return new GetReceptionistResponseDto
            {
                ReceptionistId = user.Id,
                ReceptionistName = $"{user.firstName} {user.lastName}",
                ReceptionistShift = attendances.Select(a => new SummaryShiftDto
                {
                    ShiftName =a.Shift.ShiftName,
                    ShistDate = a.Shift.ShiftDate,
                    CheckInTime = a.CheckInTime,
                    CheckOutTime = a.CheckOutTime,
                    attendanceStatus = a.attendanceStatus,
                    IsActive = user.isActive,
                    AttendanceDate = a.AttendanceDate,
                }).ToList()

            };
        }

        public async Task<List<GetReceptionistResponseDto>> GetReceptionistsAsync(int centerId)
        {
            var center = await SharedDbContext.MedicalCenters
               .FindAsync(centerId);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            return await db.Users
                .Where(u => u.role == userRole.Receptionist && u.isActive)
                .Select(r=>new GetReceptionistResponseDto { 
                
                    ReceptionistId = r.Id,
                    ReceptionistName = r.firstName+" "+r.lastName
                }) .ToListAsync();
        }
    }
}
