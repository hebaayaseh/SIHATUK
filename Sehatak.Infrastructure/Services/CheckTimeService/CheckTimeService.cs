using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.Interfaces.AuditLog;
using Sehatak.Application.Interfaces.ICheckTime;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;

namespace Sehatak.Infrastructure.Services.CheckTimeService
{
    public class CheckTimeService : ICheckTime
    {
        private readonly SharedDbContext sharedDbContext;
        private readonly TenantDbContextFactory contextFactory;
        private readonly IAuditLog auditLog;
        public CheckTimeService(SharedDbContext sharedDbContext, TenantDbContextFactory contextFactory,IAuditLog auditLog)
        {
            this.sharedDbContext = sharedDbContext;
            this.contextFactory = contextFactory;
            this.auditLog = auditLog;
        }

        public async Task<string> FinishAppointmentTime(int centerId, int userId, int appointmentId)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId && c.CenterStatus == CenterStatus.Active);
            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var doctor = await db.Doctors
                .Include(u => u.user)
                .FirstOrDefaultAsync(u => u.userId == userId && u.user.isActive);
            if (doctor == null)
                throw new BusinessException("Doctor.NotFound");

            var appointment = await db.Appointments
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == appointmentId
                                       && a.doctorId == doctor.Id
                                       && a.appointmentStatus == AppointmentStatus.InProgress);
            if (appointment == null)
                throw new BusinessException("Appointment.NotFound");
            if (appointment.actualStartTime == null)
                throw new BusinessException("Appointment.NotStartedYet");
            if (appointment.actualEndTime != null)
                throw new BusinessException("Appointment.AlreadyFinished");

            var now = DateTime.UtcNow;

            // الأول بيحجز الموعد: لو اتنين ضغطوا بنفس اللحظة واحد بس بينجح
            var rows = await db.Appointments
                .Where(a => a.Id == appointmentId
                         && a.doctorId == doctor.Id
                         && a.appointmentStatus == AppointmentStatus.InProgress
                         && a.actualEndTime == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.actualEndTime, now)
                    .SetProperty(a => a.appointmentStatus, AppointmentStatus.Completed)
                    .SetProperty(a => a.updateAt, now));
            if (rows == 0)
                throw new BusinessException("Appointment.NotFound");

            var auditEntry = auditLog.Build(
                action: "FinishAppointment",
                entityType: "Appointment",
                entityId: appointmentId,
                oldValue: new
                {
                    appointment.actualStartTime,
                    appointment.actualEndTime,
                    appointment.appointmentStatus,
                    appointment.doctorId,
                    appointment.patientId
                },
                newValue: new { actualEndTime = now, appointmentStatus = AppointmentStatus.Completed });
            if (auditEntry != null)
            {
                db.AuditLogs.Add(auditEntry);
                await db.SaveChangesAsync();
            }

            return AppointmentStatus.Completed.ToString();
        }

        public async Task<string> NextPatient(int centerId, int userId, int appointmentId)
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

            var appointment = await db.Appointments
                .FirstOrDefaultAsync(a => a.Id == appointmentId
                                     && a.appointmentStatus == AppointmentStatus.Confirmed);

            if (appointment == null)
                throw new BusinessException("Appointment.NotFound");

            if (appointment.actualStartTime != null)
                throw new BusinessException("Appointment.AlreadyStarted");

            if (appointment.appointmentDate != ClinicClock.Today)
                throw new BusinessException("Appointment.NotToday");

            var busy = await db.Appointments.AnyAsync(a => a.doctorId == appointment.doctorId
                                                && a.appointmentStatus == AppointmentStatus.InProgress);
            if (busy) throw new BusinessException("Doctor.Busy");

            var now = DateTime.UtcNow;
            var rows = await db.Appointments
                .Where(a => a.Id == appointmentId
                         && a.appointmentStatus == AppointmentStatus.Confirmed
                         && a.actualStartTime == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.appointmentStatus, AppointmentStatus.InProgress)
                    .SetProperty(a => a.actualStartTime, now)
                    .SetProperty(a => a.updateAt, now));
            if (rows == 0)
                throw new BusinessException("Appointment.AlreadyStarted");

            var auditEntry = auditLog.Build(
                "NextPatient",
                "Appointment",
                appointmentId,
                null,
                new { actualStartTime = now,
                    appointmentStatus = AppointmentStatus.InProgress 
                });

            if (auditEntry != null)
            {
                db.AuditLogs.Add(auditEntry);
                await db.SaveChangesAsync();
            }

            return AppointmentStatus.InProgress.ToString();

        }
        public async Task<string> ReceptionistCheckInAppointmentAsync(int centerId, int userId, int appointmentId)
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

            var appointment = await db.Appointments
                .FirstOrDefaultAsync(a => a.Id == appointmentId
                                     && a.appointmentStatus == AppointmentStatus.Confirmed);

            if (appointment == null)
                throw new BusinessException("Appointment.NotFound");

            if (appointment.CheckInTime != null)
                throw new BusinessException("Appointment.AlreadyCheckedIn");

            if (appointment.appointmentDate != ClinicClock.Today)
                throw new BusinessException("Appointment.NotToday");

            var now = DateTime.UtcNow;
            var rows = await db.Appointments
                .Where(a => a.Id == appointmentId
                         && a.appointmentStatus == AppointmentStatus.Confirmed
                         && a.CheckInTime == null)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.CheckInTime, now));

            if (rows == 0)
                throw new BusinessException("Appointment.AlreadyCheckedIn");

            var auditEntry = auditLog.Build(
            action: "ReceptionistCheckInAppointment",
            entityType: "Appointment",
            entityId: appointmentId,
            newValue: new
            {
                appointment.CheckInTime,
                appointment.appointmentStatus,
            });
            if (auditEntry != null)
                db.AuditLogs.Add(auditEntry);
            await db.SaveChangesAsync();

            return "تم تسجيل وصول المريض.";
        }
    }
}
