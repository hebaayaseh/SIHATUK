using Microsoft.EntityFrameworkCore;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.Infrastructure.Data;

namespace Sehatak.Infrastructure.Services.AppointmentService
{
    public partial class appointmentService
    {
        private readonly record struct PromotedWaiting(Appointment Appointment, int WaitListId);

        
        private static async Task<PromotedWaiting?> PromoteNextWaitingAsync(
            TenantDbContext db, int doctorId, DateOnly date, TimeOnly slot, int? receptionistId)
        {
            var candidates = await db.Waitlists
                .Include(w => w.Patient)
                .Where(w => w.DoctorId == doctorId && w.PreferredDate == date && w.Status == WaitlistStatus.Waiting)
                .OrderBy(w => w.CreatedAt)
                .ThenBy(w => w.Id)
                .Take(20)
                .ToListAsync();

            foreach (var candidate in candidates)
            {
                var alreadyHasAppointment = await db.Appointments.AnyAsync(a =>
                    a.patientId == candidate.PatientId && a.doctorId == doctorId && a.appointmentDate == date &&
                    (a.appointmentStatus == AppointmentStatus.Pending || a.appointmentStatus == AppointmentStatus.Confirmed ||
                     a.appointmentStatus == AppointmentStatus.CheckedIn || a.appointmentStatus == AppointmentStatus.InProgress));
                if (alreadyHasAppointment)
                    continue;

                var claimed = await db.Waitlists
                    .Where(w => w.Id == candidate.Id && w.Status == WaitlistStatus.Waiting)
                    .ExecuteUpdateAsync(s => s.SetProperty(w => w.Status, WaitlistStatus.Entered));
                if (claimed == 0)
                    continue;     // حدا أخده قبلنا: جرّب اللي بعده

                var appointment = new Appointment
                {
                    patientId = candidate.PatientId,
                    doctorId = doctorId,
                    appointmentDate = date,
                    timeSlot = slot,
                    appointmentStatus = AppointmentStatus.Confirmed,
                    ReceptionistId = receptionistId,
                    createdAt = DateTime.UtcNow,
                    updateAt = DateTime.UtcNow
                };
                db.Appointments.Add(appointment);

                db.Notifications.Add(new Notification
                {
                    UserId = candidate.Patient.NotifiableUserId,
                    Type = NotificationType.Appointment,
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow,
                    Message = $"توفر لك موعد في تاريخ {date} في الوقت {slot}"
                });

                return new PromotedWaiting(appointment, candidate.Id);
            }

            return null;
        }
    }
}
