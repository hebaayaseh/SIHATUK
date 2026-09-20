using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.DashBoardDto;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.Interfaces.IDashBoard;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;

namespace Sehatak.Infrastructure.Services.DashBoardService
{
    public class AppointmentDashBoard : IApointmentDashBoard
    {
        private readonly SharedDbContext sharedDbContext;
        private readonly TenantDbContextFactory contextFactory;
        public AppointmentDashBoard(SharedDbContext sharedDbContext, TenantDbContextFactory contextFactory)
        {
            this.sharedDbContext = sharedDbContext;
            this.contextFactory = contextFactory;
        }

        public async Task<AppointmentsSummaryDto> GetCenterAppointmentsSummaryAsync(int centerId, DateOnly? date = null)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId 
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            var targetDate = date ?? ClinicClock.Today;

            using var db = contextFactory.CreateForCenter(centerId);

            var appointments = await db.Appointments
                .Where(a => a.appointmentDate == targetDate)
                .Select(a => a.appointmentStatus)
                .ToListAsync();

            var waitlistCount = await db.Waitlists
                .CountAsync(w => w.PreferredDate == targetDate 
                            && w.Status == WaitlistStatus.Waiting);

            return new AppointmentsSummaryDto
            {
                Date = targetDate,
                Total = appointments.Count,
                Confirmed = appointments.Count(s => s == AppointmentStatus.Confirmed),
                Completed = appointments.Count(s => s == AppointmentStatus.Completed),
                Cancelled = appointments.Count(s => s == AppointmentStatus.Cancelled),
                NoShow = appointments.Count(s => s == AppointmentStatus.NoShow),
                WaitlistCount = waitlistCount
            };
        }

        public async Task<PagedResult<PatientsAppointmentResponseDto>> GetPatientAppointmentsAsync(int centerId, int userId, DateOnly date, int? subPatientId, PagedRequest request, AppointmentStatus status)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var patient = await db.Patients
                .Include(u=>u.user)
                .FirstOrDefaultAsync(u => u.userId == userId
                                     && u.user.isActive);

            if (patient == null)
                throw new BusinessException("Patient.NotFound");

            Patient actingPatient = patient;

            if (subPatientId.HasValue)
            {
                var subPatient = await db.Patients
                    .FirstOrDefaultAsync(s => s.ParentPatientId == actingPatient.patientId
                                        && s.patientId == subPatientId);

                if (subPatient == null)
                    throw new BusinessException("SubPatient.NotFound");

                actingPatient = subPatient;
            }

            var query = db.Appointments
                .Where(a => a.appointmentDate == date
                       && a.patientId == actingPatient.patientId
                       && a.appointmentStatus == status)
                .OrderBy(a => a.timeSlot)
                .Select(a => new PatientsAppointmentResponseDto
                {
                    AppointmentId = a.Id,
                    PatientId = a.patientId,
                    PatientName = actingPatient.userId != null
                        ? actingPatient.user.firstName + " " + actingPatient.user.lastName
                        : actingPatient.FirstName + " " + actingPatient.LastName,
                    DoctorId = a.doctorId,
                    DoctorName = a.Doctor.user.firstName + " " + a.Doctor.user.lastName,
                    TimeSlot = a.timeSlot,
                    CheckInTime = a.CheckInTime,
                    CheckOutTime = a.CheckOutTime,
                    IsFollowUp = a.IsFollowUp,
                    ActualStart = a.actualStartTime,
                    ActualEnd = a.actualEndTime
                });

            return await query.ToPagedResultAsync(request.PageNumber, request.PageSize);
        }

        public async Task<PagedResult<ReceptionistAppointmentResponseDto>> GetReceptionistAppointmentsAsync(int centerId, int userId, DateOnly date, PagedRequest request)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var user = await db.Users
                .FirstOrDefaultAsync(u => u.Id == userId
                                     && u.isActive);

            if (user == null)
                throw new BusinessException("User.NotFound");

            var query = db.Appointments
                .Where(a => a.appointmentDate == date
                       && a.appointmentStatus == AppointmentStatus.Confirmed)
                .OrderBy(a => a.timeSlot)
                .Select(a => new ReceptionistAppointmentResponseDto
                {
                    AppointmentId = a.Id,
                    PatientId = a.patientId,
                    PatientName = a.Patient.userId != null
                        ? a.Patient.user.firstName + " " + a.Patient.user.lastName
                        : a.Patient.FirstName + " " + a.Patient.LastName,
                    DoctorId = a.doctorId,
                    DoctorName = a.Doctor.user.firstName + " " + a.Doctor.user.lastName,
                    TimeSlot = a.timeSlot,
                    CheckInTime = a.CheckInTime,
                    CheckOutTime = a.CheckOutTime,
                    IsFollowUp = a.IsFollowUp,
                    BillAmount = a.BillAmount,
                    PaymentStatus = a.Payment != null ? a.Payment.Status.ToString() : null
                });

            return await query.ToPagedResultAsync(request.PageNumber, request.PageSize);
        }
    }
}
