using DocumentFormat.OpenXml.Office2016.Excel;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.AddDoctorDailyHour;
using Sehatak.Application.DTOs.AddDoctorDailyHourDto;
using Sehatak.Application.DTOs.DoctorDto;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.Interfaces.AddDoctorDailyHours;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.PostponeEnums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;

namespace Sehatak.Infrastructure.Services.AddStaff
{
    public class DoctorDailyHoursService : IDoctorDailyHours
    {
        private readonly SharedDbContext sharedDbContext;
        private readonly TenantDbContextFactory contextFactory;
        public DoctorDailyHoursService(SharedDbContext sharedDbContext, TenantDbContextFactory contextFactory)
        {
            this.sharedDbContext = sharedDbContext;
            this.contextFactory = contextFactory;
        }

        public async Task<AddDoctorDailyHoursResponse> AddDoctorDailyHoursAsync(int centerId, int userId, int doctorId, AddDoctorDailyHoursRequest request)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var admin = await db.Users
                .FirstOrDefaultAsync(u => u.Id == userId && u.role == userRole.Admin && u.isActive);

            if (admin == null)
                throw new BusinessException("Auth.Forbidden");


            var doctor = await db.Doctors
                .Include(u => u.user)
                .FirstOrDefaultAsync(d => d.Id == doctorId && d.user.isActive);

            if (doctor == null)
                throw new BusinessException("Doctor.NotFound");

            var hasConflict = await db.DoctorSchedules
              .AnyAsync(s => s.DoctorId == doctorId
                    && s.DayOfWeek == request.DayOfWeek
                    && request.StartTime < s.EndTime
                    && request.EndTime > s.StartTime);
            if (hasConflict)
                throw new BusinessException("Schedule.Conflict");

            var doctorScheduale = new Doctorschedule
            {
                DoctorId = doctorId,
                DayOfWeek = request.DayOfWeek,
                SlotDurationMinutes = request.SlotDurationMinutes,
                StartTime = request.StartTime,
                EndTime = request.EndTime

            };

            await db.DoctorSchedules.AddAsync(doctorScheduale);
            await db.SaveChangesAsync();

            return new AddDoctorDailyHoursResponse
            {
                DoctorId = doctorId,
                DayOfWeek = doctorScheduale.DayOfWeek,
                SlotDurationMinutes = request.SlotDurationMinutes,
                StartTime = request.StartTime,
                EndTime = request.EndTime
            };
        }

        public async Task<string> CancleDailyHoursAsync(int centerId, int userId, DateOnly date)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId && c.CenterStatus == CenterStatus.Active);
            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var doctor = await db.Doctors
             .Include(d => d.user)
             .FirstOrDefaultAsync(d => d.userId == userId && d.user.isActive); 
            if (doctor == null)
                throw new BusinessException("Doctor.NotFound");

            var doctorId = doctor.Id;

            if (doctor == null)
                throw new BusinessException("Doctor.NotFound");

            var schedule = await db.DoctorSchedules
                .FirstOrDefaultAsync(d => d.DoctorId == doctorId
                                     && d.IsActive
                                     && d.DayOfWeek == date.DayOfWeek);

            if (schedule == null)
                throw new BusinessException("Schedule.NotFound");


            var alreadyBlocked = await db.DoctorBlockedDays
                .AnyAsync(d => d.doctorId == doctorId && d.date == date && d.isBlocked);
            if (alreadyBlocked)
                throw new BusinessException("Doctor.DayAlreadyBlocked");

            var appointments = await db.Appointments
                .Include(a => a.Patient).ThenInclude(p => p.user)
                .Where(a => a.doctorId == doctorId
                         && a.appointmentDate == date
                         && a.appointmentStatus == AppointmentStatus.Confirmed)
                .OrderBy(a => a.timeSlot)
                .ToListAsync();

            foreach (var appointment in appointments)
            {
                appointment.appointmentStatus = AppointmentStatus.Cancelled;

                db.PostponedServices.Add(new PostponedService
                {
                    PatientId = appointment.patientId,
                    CreatedByUserId = doctor.user.Id,
                    Type = PostponeType.DoctorAppointment,
                    AppointmentId = appointment.Id,
                    Reason = "إلغاء مواعيد اليوم من قبل الطبيب.",
                    Status = PostponeStatus.Active,
                });

                db.Notifications.Add(new Notification
                {
                    UserId = (int)appointment.Patient.userId,
                    Message = "نحيطكم علمًا بأنه تم إلغاء موعدكم اليوم. يرجى حجز موعد جديد.",
                    CreatedAt = DateTime.UtcNow,
                    Type = NotificationType.Cancellation,
                    IsRead = false
                });
            }

            db.DoctorBlockedDays.Add(new DoctorBlockedDay
            {
                doctorId = doctorId,
                date = date,
                Reason = "إلغاء من قبل الطبيب",
                isBlocked = true
            });

            var waitList = await db.Waitlists
                 .Include(w => w.Patient).ThenInclude(p => p.user)
                 .Where(d => d.DoctorId == doctorId
                  && d.Status == WaitlistStatus.Waiting
                  && d.PreferredDate == date)
                 .ToListAsync();

            foreach (var item in waitList)
            {
                item.Status = WaitlistStatus.Exited;

                db.Notifications.Add(new Notification
                {
                    UserId = (int)item.Patient.userId,
                    Message = "تم تعديل جدول الطبيب لهذا اليوم، يرجى محاولة الحجز من جديد على الموعد الجديد.",
                    CreatedAt = DateTime.UtcNow,
                    Type = NotificationType.Cancellation,
                    IsRead = false
                });
            }

            await db.SaveChangesAsync();

            return appointments.Any()
                ? "تم إلغاء مواعيد اليوم بنجاح ومنع الحجز الجديد لهذا التاريخ."
                : "تم حظر هذا اليوم من الحجز بنجاح.";
        }

        public async Task<List<GetDoctorDailyHoursResponse>> GetDoctorDailyHoursAsync(int centerId, int doctorId)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId 
                                     && c.CenterStatus == CenterStatus.Active);
            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var doctor = await db.Doctors
                .Include(d => d.user)
                .FirstOrDefaultAsync(d => d.Id == doctorId && d.user.isActive);

            if (doctor == null)
                throw new BusinessException("Doctor.NotFound");

            var doctorSchedual = await db.DoctorSchedules
                .FirstOrDefaultAsync(d => d.DoctorId == doctorId);

            if (doctorSchedual == null)
                throw new BusinessException("Schedule.NotFound");

            var Scheduales = await db.DoctorSchedules
                .Where(d => d.DoctorId == doctorId)
                .Select(n=>new GetDoctorDailyHoursResponse
                {
                    Id = n.Id,
                    DayOfWeek = n.DayOfWeek,
                    IsActive = n.IsActive,
                    DoctorId = n.DoctorId,
                    EndTime = n.EndTime,
                    StartTime = n.StartTime,
                    SlotDurationMinutes = n.SlotDurationMinutes,
                }).ToListAsync();

            return Scheduales;

        }

        public async Task<UpdateDoctorDailyHoursResponse> UpdateDoctorDailyHoursAsync(
        int centerId, int userId, int doctorId, UpdateDoctorDailyHousrRequest request)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId && c.CenterStatus == CenterStatus.Active);
            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var admin = await db.Users
                .FirstOrDefaultAsync(u => u.Id == userId && u.role == userRole.Admin && u.isActive);
            if (admin == null)
                throw new BusinessException("Auth.Forbidden");

            var doctor = await db.Doctors
                .Include(d => d.user)
                .FirstOrDefaultAsync(d => d.Id == doctorId && d.user.isActive);
            if (doctor == null)
                throw new BusinessException("Doctor.NotFound");

            var doctorSchedual = await db.DoctorSchedules
                .FirstOrDefaultAsync(d => d.DoctorId == doctorId
                    && d.Id == request.schedualId
                    && d.IsActive);
            if (doctorSchedual == null)
                throw new BusinessException("General.NotFound");


            var hasConflict = await db.DoctorSchedules
                .AnyAsync(s => s.DoctorId == doctorId
                    && s.Id != doctorSchedual.Id
                    && s.IsActive
                    && s.DayOfWeek == request.DayOfWeek
                    && request.StartTime < s.EndTime
                    && request.EndTime > s.StartTime);
            if (hasConflict)
                throw new BusinessException("Schedule.Conflict");


            var affectedAppointments = await db.Appointments
                .Include(a => a.Patient)
                .ThenInclude(p => p.user)
                .Where(a => a.doctorId == doctorId
                    && a.appointmentStatus == AppointmentStatus.Confirmed
                    && a.appointmentDate.DayOfWeek == doctorSchedual.DayOfWeek
                    && a.timeSlot.HasValue
                    && a.timeSlot.Value >= doctorSchedual.StartTime
                    && a.timeSlot.Value < doctorSchedual.EndTime)
                .OrderBy(a => a.appointmentDate)
                .ToListAsync();

            foreach (var appointment in affectedAppointments)
            {
                appointment.appointmentStatus = AppointmentStatus.Postponed;
                db.PostponedServices.Add(new PostponedService
                {
                    PatientId = appointment.patientId,
                    CreatedByUserId = userId, 
                    Type = PostponeType.DoctorAppointment,
                    AppointmentId = appointment.Id,
                    Reason = "تعديل جدول دوام الطبيب من قبل الإدارة",
                    Status = PostponeStatus.Active,
                });
                db.Notifications.Add(new Notification
                {
                    UserId = (int)appointment.Patient.userId,
                    Message = "تم تغيير مواعيد دوام الطبيب، يرجى إعادة جدولة الموعد في أقرب وقت",
                    CreatedAt = DateTime.UtcNow,
                    Type = NotificationType.Cancellation,
                    IsRead = false
                });
                //await NotifyPatientPostponeAsync(appointment.Patient.user);
            }
            var affectedDates = affectedAppointments
                  .Select(a => a.appointmentDate)
                  .Distinct()
                  .ToList();

            var waitList = await db.Waitlists
                 .Include(w => w.Patient)
                 .ThenInclude(u => u.user)
                 .Where(w => w.DoctorId == doctorId
                  && w.Status == WaitlistStatus.Waiting
                  && affectedDates.Contains(w.PreferredDate))
                  .ToListAsync();

            foreach (var item in waitList)
            {
                item.Status = WaitlistStatus.Exited;

                db.Notifications.Add(new Notification
                {
                    UserId = (int)item.Patient.userId,
                    Message = "تم تعديل جدول الطبيب لهذا اليوم، يرجى محاولة الحجز من جديد على الموعد الجديد.",
                    CreatedAt = DateTime.UtcNow,
                    Type = NotificationType.Cancellation,
                    IsRead = false
                });
            }

            doctorSchedual.IsActive = false;

            var newSchedule = new Doctorschedule
            {
                DoctorId = doctorId,
                DayOfWeek = request.DayOfWeek,
                SlotDurationMinutes = request.SlotDurationMinutes,
                StartTime = request.StartTime,
                EndTime = request.EndTime
            };
            await db.DoctorSchedules.AddAsync(newSchedule);

            await db.SaveChangesAsync();

            return new UpdateDoctorDailyHoursResponse
            {
                DoctorId = doctorId,
                DayOfWeek = newSchedule.DayOfWeek,
                SlotDurationMinutes = request.SlotDurationMinutes,
                StartTime = request.StartTime,
                EndTime = request.EndTime
            };
        }

        public async Task<DoctorAppointmentResponse> GetDoctorAppointmentsForDayAsync(int centerId, int userId, DateOnly? date)
        {
            if (date != null && date < DateOnly.FromDateTime(DateTime.UtcNow))
                throw new BusinessException("Date.Invalid");

            date ??= DateOnly.FromDateTime(DateTime.UtcNow);

            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId && c.CenterStatus == CenterStatus.Active);
            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var doctor = await db.Doctors
                .Include(u => u.user)
                .FirstOrDefaultAsync(d => d.userId == userId && d.user.isActive);
            if (doctor == null)
                throw new BusinessException("Doctor.NotFound");

            var appointments = await db.Appointments
                .Where(a => a.doctorId == doctor.Id
                       && a.appointmentDate == date
                       && a.appointmentStatus == AppointmentStatus.Confirmed)
                .OrderBy(a => a.timeSlot)
                .Select(a => new AppointmentSummaryDto
                {
                    appointmentId = a.Id,
                    patientId = a.patientId,
                    patientName = $"{a.Patient.user.firstName} {a.Patient.user.lastName}",
                    date = a.appointmentDate,
                    timeSlot = (TimeOnly)a.timeSlot,
                })
                .ToListAsync();

            return new DoctorAppointmentResponse
            {
                appointments = appointments
            };
        }

        //private async Task NotifyPatientPostponeAsync(User user)
        //{
        //    await notificationService.CreateAsync(
        //        user.Id,
        //        NotificationType.Cancellation,
        //        "تم تعديل جدول الدكتور، وموعدك يحتاج لإعادة جدولة. يرجى مراجعة التطبيق لاختيار موعد جديد.");

        //    if (!string.IsNullOrWhiteSpace(user.phoneNumber))
        //        await whatsAppService.SendMessageAsync(user.phoneNumber,
        //            "تم تعديل جدول الدكتور، وموعدك يحتاج لإعادة جدولة. يرجى مراجعة التطبيق لاختيار موعد جديد.");
        //}
    }
}
