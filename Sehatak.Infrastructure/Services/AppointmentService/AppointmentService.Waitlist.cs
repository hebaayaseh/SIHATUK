using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.AppointmentDto;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;

namespace Sehatak.Infrastructure.Services.AppointmentService
{
    public partial class appointmentService
    {
        
        public async Task<ReceptionistEnterFromWaitlistResponse> ReceptionistEnterFromWaitlistAsync(
            int centerId, int doctorId, int userId, ReceptionistEnterFromWaitlistRequest request)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId && c.CenterStatus == CenterStatus.Active);
            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var receptionist = await db.Users
                .FirstOrDefaultAsync(u => u.Id == userId && u.isActive && u.role == userRole.Receptionist);
            if (receptionist == null)
                throw new BusinessException("Receptionist.NotFound");

            var doctor = await db.Doctors
                .Include(d => d.user)
                .FirstOrDefaultAsync(d => d.Id == doctorId && d.user.isActive);
            if (doctor == null)
                throw new BusinessException("Doctor.NotFound");

            var entry = await db.Waitlists
                .Include(w => w.Patient)
                .FirstOrDefaultAsync(w => w.Id == request.WaitListId && w.DoctorId == doctorId);
            if (entry == null)
                throw new BusinessException("WaitList.NotFound");

            if (entry.Status != WaitlistStatus.Waiting)
                throw new BusinessException("Waitlist.NotWaiting");

            var today = ClinicClock.Today;
            var date = entry.PreferredDate;
            if (date < today)
                throw new BusinessException("Date.Invalid");

            var wholeDayBlocked = await db.DoctorBlockedDays
                .AnyAsync(bd => bd.doctorId == doctorId && bd.isBlocked && bd.date == date && bd.timeSlot == null);
            if (wholeDayBlocked)
                throw new BusinessException("Doctor.DayBlocked");

            var schedule = await db.DoctorSchedules
                .FirstOrDefaultAsync(s => s.DoctorId == doctorId && s.IsActive && s.DayOfWeek == date.DayOfWeek);
            if (schedule == null)
                throw new BusinessException("Schedule.NotFound");

            var duration = schedule.SlotDurationMinutes
                ?? throw new BusinessException("Schedule.SlotDurationMissing");

            var alreadyHasAppointment = await db.Appointments.AnyAsync(a =>
                a.patientId == entry.PatientId && a.doctorId == doctorId && a.appointmentDate == date &&
                (a.appointmentStatus == AppointmentStatus.Pending || a.appointmentStatus == AppointmentStatus.Confirmed ||
                 a.appointmentStatus == AppointmentStatus.CheckedIn || a.appointmentStatus == AppointmentStatus.InProgress));
            if (alreadyHasAppointment)
                throw new BusinessException("Appointment.AlreadyExists");

            var occupied = await db.Appointments
                .Where(a => a.doctorId == doctorId && a.appointmentDate == date && a.timeSlot.HasValue
                            // نفس الحالات اللي بتحجز الموعد بالـ unique index: كل شي ما عدا الملغي/المؤجل
                            && a.appointmentStatus != AppointmentStatus.Cancelled
                            && a.appointmentStatus != AppointmentStatus.NoShow
                            && a.appointmentStatus != AppointmentStatus.Rescheduled
                            && a.appointmentStatus != AppointmentStatus.Postponed)
                .Select(a => a.timeSlot!.Value)
                .ToListAsync();

            var blocked = await db.DoctorBlockedDays
                .Where(bd => bd.doctorId == doctorId && bd.isBlocked && bd.date == date && bd.timeSlot.HasValue)
                .Select(bd => bd.timeSlot!.Value)
                .ToListAsync();

            var taken = new HashSet<TimeOnly>(occupied);
            taken.UnionWith(blocked);

            var theoretical = generateTheoreticalSlots
                .GenerateTheoreticalSlot(schedule.StartTime, schedule.EndTime, duration)
                .Where(s => s.HasValue).Select(s => s!.Value)
                .ToList();

            var nowTime = TimeOnly.FromDateTime(ClinicClock.Now);
            TimeOnly slot;

            if (request.TimeSlot.HasValue)
            {
                slot = request.TimeSlot.Value;

                if (!theoretical.Contains(slot))
                    throw new BusinessException("Waitlist.InvalidSlot");
                if (taken.Contains(slot))
                    throw new BusinessException("Appointment.SlotTaken");
                if (date == today && slot < nowTime)
                    throw new BusinessException("Waitlist.InvalidSlot");
            }
            else
            {
                var firstFree = theoretical
                    .Where(s => !taken.Contains(s) && (date != today || s >= nowTime))
                    .OrderBy(s => s)
                    .Cast<TimeOnly?>()
                    .FirstOrDefault();

                if (firstFree.HasValue)
                {
                    slot = firstFree.Value;
                }
                else
                {
                    // اليوم ممتلئ: بعد آخر موعد محجوز (أو بداية الدوام إذا ما في ولا موعد)
                    slot = occupied.Count > 0 ? occupied.Max().AddMinutes(duration) : schedule.StartTime;
                    // لا بنحط المريض بموعد محجوز/محظور، ولا بوقت فات اليوم
                    var guard = 0;
                    while ((taken.Contains(slot) || (date == today && slot < nowTime)) && guard++ < 200)
                    {
                        var next = slot.AddMinutes(duration);
                        if (next <= slot)   // لفّ حول منتصف الليل
                            throw new BusinessException("Waitlist.InvalidSlot");
                        slot = next;
                    }

                    // AddMinutes بتلف حول منتصف الليل: إذا رجع لورا يعني ما في مكان باليوم
                    if (occupied.Count > 0 && slot <= occupied.Max())
                        throw new BusinessException("Waitlist.InvalidSlot");
                }
            }

            var isOverflow = !theoretical.Contains(slot);
            var checkIn = date == today;

            using var transaction = await db.Database.BeginTransactionAsync();

            // نحجز صف الويت ليست بشكل ذري: إذا جوب ثاني (أو استقبال ثاني) أخده قبلنا، بنوقف
            var claimed = await db.Waitlists
                .Where(w => w.Id == entry.Id && w.Status == WaitlistStatus.Waiting)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(w => w.Status, WaitlistStatus.Entered)
                    .SetProperty(w => w.ReceptionistId, (int?)userId));
            if (claimed == 0)
                throw new BusinessException("Waitlist.NotWaiting");

            var appointment = new Appointment
            {
                patientId = entry.PatientId,
                doctorId = doctorId,
                appointmentDate = date,
                timeSlot = slot,
                appointmentStatus = AppointmentStatus.Confirmed,
                ReceptionistId = userId,
                CheckInTime = checkIn ? DateTime.UtcNow : null,
                createdAt = DateTime.UtcNow,
                updateAt = DateTime.UtcNow
            };
            db.Appointments.Add(appointment);

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // نفس الموعد انحجز بنفس اللحظة (unique index): الـ transaction بترجع كل شي
                throw new BusinessException("Appointment.SlotTaken");
            }

            entry.PromotedAppointmentId = appointment.Id;

            db.Notifications.Add(new Notification
            {
                UserId = entry.Patient.NotifiableUserId,
                Type = NotificationType.Appointment,
                IsRead = false,
                CreatedAt = DateTime.UtcNow,
                Message = $"تم إدخالك من قائمة الانتظار: موعدك بتاريخ {date} الساعة {slot}."
            });

            var auditEntry = auditLog.Build("EnterFromWaitlist", "Appointment", appointment.Id,
                newValue: new { WaitListId = entry.Id, appointment.doctorId, appointment.patientId, appointment.appointmentDate, appointment.timeSlot, isOverflow });
            if (auditEntry != null)
                db.AuditLogs.Add(auditEntry);

            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            return new ReceptionistEnterFromWaitlistResponse
            {
                Success = true,
                Message = isOverflow
                    ? "تم إدخال المريض من قائمة الانتظار بعد آخر موعد محجوز."
                    : "تم إدخال المريض من قائمة الانتظار.",
                AppointmentId = appointment.Id,
                Date = date,
                TimeSlot = slot,
                IsOverflow = isOverflow,
                CheckedIn = checkIn
            };
        }

        public async Task<string> ReceptionistRemoveFromWaitlistAsync(int centerId, int doctorId, int userId, int waitListId)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId && c.CenterStatus == CenterStatus.Active);
            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var receptionist = await db.Users
                .FirstOrDefaultAsync(u => u.Id == userId && u.isActive && u.role == userRole.Receptionist);
            if (receptionist == null)
                throw new BusinessException("Receptionist.NotFound");

            var claimed = await db.Waitlists
                .Where(w => w.Id == waitListId && w.DoctorId == doctorId && w.Status == WaitlistStatus.Waiting)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(w => w.Status, WaitlistStatus.Cancelled)
                    .SetProperty(w => w.ReceptionistId, (int?)userId));

            if (claimed == 0)
            {
                var exists = await db.Waitlists.AnyAsync(w => w.Id == waitListId && w.DoctorId == doctorId);
                throw new BusinessException(exists ? "Waitlist.NotWaiting" : "WaitList.NotFound");
            }

            var auditEntry = auditLog.Build("RemoveFromWaitlist", "Waitlist", waitListId, newValue: new { doctorId });
            if (auditEntry != null)
            {
                db.AuditLogs.Add(auditEntry);
                await db.SaveChangesAsync();
            }

            return "تم إزالة المريض من قائمة الانتظار.";
        }
    }
}
