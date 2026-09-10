using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.EmergencyDto;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.Interfaces.IEmerngency;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;

namespace Sehatak.Infrastructure.Services.EmergencyService
{
    public class EmergencyService : IEmerngency
    {
        private readonly SharedDbContext sharedDbContext;
        private readonly TenantDbContextFactory contextFactory;
        public EmergencyService(SharedDbContext sharedDbContext, TenantDbContextFactory contextFactory)
        {
            this.sharedDbContext = sharedDbContext;
            this.contextFactory = contextFactory;
        }

        public async Task<EmergencyResponseDto> EditPatientEmergencyAsync(int centerId, int userId, UpdateEmergencyRequestDto request)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var emergency = await db.EmergencyCases
                .FirstOrDefaultAsync(e => e.Id == request.EmergencyId);

            if (emergency == null)
                throw new BusinessException("Emergency.NotFound");

            if(request.PatientName!=null)
                emergency.PatientName = request.PatientName;

            if (request.IsInsurance != null)
                emergency.IsInsurance = (bool)request.IsInsurance;

            if (request.AmountPaid != null)
                emergency.AmountPaid = (decimal)request.AmountPaid;

            emergency.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync();
            return new EmergencyResponseDto 
            { 
                Id = emergency.Id,
                PatientName = emergency.PatientName,
                AmountPaid = emergency.AmountPaid,
                DoctorUserId = emergency.DoctorUserId,
                IsInsurance = emergency.IsInsurance,
                ReceptionistId = emergency.ReceptionistId,
                CreatedAt = emergency.CreatedAt,
                UpdatedAt = emergency.UpdatedAt
            };


        }

        public async Task<PagedResult<GetDoctorGeneralResponseDto>> GetDoctorGeneralAsync(int centerId, PagedRequest request)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var date = DateOnly.FromDateTime(DateTime.UtcNow);
            var now = DateTime.UtcNow;

            var query =  db.StaffAttendances
               .Where(t => t.Staff.role == userRole.GeneralDoctor
                                   && t.AttendanceDate == date
                                   && t.attendanceStatus != AttendanceStatus.Absent
                                   && t.attendanceStatus != AttendanceStatus.OnLeave)
                .OrderBy(f => f.AttendanceDate)
                .Select(n => new GetDoctorGeneralResponseDto
                {
                    DoctorUserId = n.UserId,
                    DoctorName = $"{n.Staff.firstName} {n.Staff.lastName}",
                    PhoneNumber = n.Staff.phoneNumber,
                    Date = n.AttendanceDate,
                    Email = n.Staff.email,
                    ShiftName = n.Shift.ShiftName
                });
            return await query.ToPagedResultAsync(request.PageNumber, request.PageSize);
        }

        public async Task<PagedResult<EmergencyResponseDto>> GetPatientEmergencyAsync(int centerId, PagedRequest request)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var query = db.EmergencyCases
                .OrderBy(c=>c.CreatedAt)
                .Select(n => new EmergencyResponseDto
                {
                    Id = n.Id,
                    PatientName = n.PatientName,
                    DoctorUserId = n.DoctorUserId,
                    ReceptionistId = n.ReceptionistId,
                    AmountPaid = n.AmountPaid,
                    IsInsurance = n.IsInsurance,
                    CreatedAt = n.CreatedAt,
                    UpdatedAt = n.UpdatedAt
                });
            return await query.ToPagedResultAsync(request.PageNumber, request.PageSize);
        }

        public async Task<EmergencyResponseDto> RegisterPatientEmergencyAsync(int centerId, int userId, EmergencyRequestDto request)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var receptionist = await db.Users
                .FirstOrDefaultAsync(r => r.Id == userId
                                     && r.isActive
                                     && r.role == userRole.Receptionist);

            if (receptionist == null)
                throw new BusinessException("Receptionist.NotFound");

            var doctor = await db.Users
                .FirstOrDefaultAsync(d => d.Id == request.DoctorUserId
                                     && d.role == userRole.GeneralDoctor
                                     && d.isActive);

            if (doctor == null)
                throw new BusinessException("Doctor.NotFound");

            var date = DateOnly.FromDateTime(DateTime.UtcNow);
            var now = DateTime.UtcNow;

            var shift = await db.StaffAttendances
               .FirstOrDefaultAsync(t => t.UserId == request.DoctorUserId
                                   && t.AttendanceDate == date
                                   && t.attendanceStatus != AttendanceStatus.Absent
                                   && t.attendanceStatus != AttendanceStatus.OnLeave);

            if (shift == null)
                throw new BusinessException("DoctorShift.NotFound");

            var patientEmergency = new EmergencyCase
            {
                PatientName = request.PatientName,
                DoctorUserId = request.DoctorUserId,
                ReceptionistId = userId,
                IsInsurance = request.IsInsurance,
                AmountPaid = request.AmountPaid,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await db.EmergencyCases.AddAsync(patientEmergency);
            await db.SaveChangesAsync();

            return new EmergencyResponseDto
            { 
                Id = patientEmergency.Id,
                ReceptionistId = userId,
                PatientName = request.PatientName,
                DoctorUserId = request.DoctorUserId,
                IsInsurance = request.IsInsurance,
                AmountPaid = request.AmountPaid,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
        }
    }
}
