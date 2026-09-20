using DocumentFormat.OpenXml.Office2016.Excel;
using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.AddDoctorDailyHour;
using Sehatak.Application.DTOs.AddDoctorDailyHourDto;
using Sehatak.Application.DTOs.DoctorDailyHourDto;
using Sehatak.Application.DTOs.DoctorDto;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.Interfaces.AddDoctorDailyHours;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.PostponeEnums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;
using Microsoft.Extensions.Caching.Memory;
using System.Collections.Concurrent;

namespace Sehatak.Infrastructure.Services.AddStaff
{
    public class DoctorDailyHoursService : IDoctorDailyHours
    {
        private readonly SharedDbContext sharedDbContext;
        private readonly TenantDbContextFactory contextFactory;
        private readonly IMemoryCache cache;
        public DoctorDailyHoursService(SharedDbContext sharedDbContext, TenantDbContextFactory contextFactory,IMemoryCache cache)
        {
            this.sharedDbContext = sharedDbContext;
            this.contextFactory = contextFactory;
            this.cache = cache;
        }
        private static string CacheKey(int centerId, int doctorId, int pageNumber, int pageSize) =>
            $"doctorschedule:{centerId}:{doctorId}:{pageNumber}:{pageSize}";

        private static readonly ConcurrentDictionary<(int centerId, int doctorId), ConcurrentBag<string>> cacheKeysByDoctor = new();

        private void TrackKey(int centerId, int doctorId, string key)
        {
            var bag = cacheKeysByDoctor.GetOrAdd((centerId, doctorId), _ => new ConcurrentBag<string>());
            bag.Add(key);
        }

        private void InvalidateDoctorScheduleCache(int centerId, int doctorId)
        {
            if (!cacheKeysByDoctor.TryRemove((centerId, doctorId), out var keys))
                return;

            foreach (var key in keys)
                cache.Remove(key);
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
            InvalidateDoctorScheduleCache(centerId,doctorId);

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
             .FirstOrDefaultAsync(d => d.userId == userId
                                  && d.user.isActive);

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
                .AnyAsync(d => d.doctorId == doctorId 
                          && d.date == date 
                          && d.isBlocked 
                          && d.timeSlot == null);

            if (alreadyBlocked)
                throw new BusinessException("Doctor.DayAlreadyBlocked");

            var appointments = await db.Appointments
                .Include(a => a.Patient).ThenInclude(p => p.user)
                .Where(a => a.doctorId == doctorId
                         && a.appointmentDate == date
                         && a.appointmentStatus == AppointmentStatus.Confirmed)
                .OrderBy(a => a.timeSlot)
                .ToListAsync();

            var appointmentIds = appointments.Select(a => a.Id).ToList();

            var relatedFollowUps = await db.FollowUps
               .Where(f => f.DoctorId == doctorId
                      && f.Status == FollowUpStatus.Booked
                      && f.ScheduledAppointmentId != null
                      && appointmentIds.Contains(f.ScheduledAppointmentId.Value))
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
                    UserId = appointment.Patient.NotifiableUserId,
                    Message = "نحيطكم علمًا بأنه تم إلغاء موعدكم اليوم. يرجى حجز موعد جديد.",
                    CreatedAt = DateTime.UtcNow,
                    Type = NotificationType.Cancellation,
                    IsRead = false
                });
                var followUp = relatedFollowUps.FirstOrDefault(f => f.ScheduledAppointmentId == appointment.Id);
                if (followUp != null)
                {
                    followUp.Status = FollowUpStatus.Pending;
                    followUp.AllowFollowUpDate = followUp.AllowFollowUpDate?.AddDays(8);
                }
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
                    UserId = item.Patient.NotifiableUserId,
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

        public async Task<Application.Common.PagedResult<GetDoctorDailyHoursResponse>> GetDoctorDailyHoursAsync(int centerId, int doctorId, PagedRequest request)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId 
                                     && c.CenterStatus == CenterStatus.Active);
            if (center == null)
                throw new BusinessException("Center.NotFound");

            var key = CacheKey(centerId, doctorId, request.PageNumber, request.PageSize);
            if (cache.TryGetValue(key, out Application.Common.PagedResult<GetDoctorDailyHoursResponse>? cached))
                return cached!;

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

            var query =  db.DoctorSchedules
                .Where(d => d.DoctorId == doctorId)
                .OrderBy(d => d.DayOfWeek)
                .Select(n=>new GetDoctorDailyHoursResponse
                {
                    Id = n.Id,
                    DayOfWeek = n.DayOfWeek,
                    IsActive = n.IsActive,
                    DoctorId = n.DoctorId,
                    EndTime = n.EndTime,
                    StartTime = n.StartTime,
                    SlotDurationMinutes = n.SlotDurationMinutes,
                });

            var result = await query.ToPagedResultAsync(request.PageNumber, request.PageSize);

            cache.Set(key, result, TimeSpan.FromMinutes(10));
            TrackKey(centerId,doctorId, key);

            return result;
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
                    UserId = appointment.Patient.NotifiableUserId,
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
                    UserId = item.Patient.NotifiableUserId,
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
            InvalidateDoctorScheduleCache(centerId, doctorId);

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
            if (date != null && date < ClinicClock.Today)
                throw new BusinessException("Date.Invalid");

            date ??= ClinicClock.Today;

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
                .OrderBy(a => a.timeSlot.HasValue)
                .Select(a => new AppointmentSummaryDto
                {
                    appointmentId = a.Id,
                    patientId = a.patientId,
                    patientName = a.Patient.userId!=null
                    ?$"{a.Patient.user.firstName} {a.Patient.user.lastName}"
                    :$"{a.Patient.FirstName} {a.Patient.LastName}",
                    date = a.appointmentDate,
                    timeSlot = a.timeSlot!.Value,
                    IsfollowUp = a.IsFollowUp
                })
                .ToListAsync();

            return new DoctorAppointmentResponse
            {
                appointments = appointments
            };
        }

        public async Task<Application.Common.PagedResult<GetDoctorsBlockDaysResponseDto>> GetDoctorsBlockDayAsync(int centerId, int userId, DateOnly date, PagedRequest request)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId && c.CenterStatus == CenterStatus.Active);
            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var user = await db.Users
                .FirstOrDefaultAsync(u => u.Id == userId
                                     && u.isActive);

            if (user == null)
                throw new BusinessException("User.NotFound");

            var query = db.DoctorBlockedDays
                .Include(d=>d.Doctor)
                .Where(d => d.date == date)
                .OrderByDescending(d => d.CreatedAt)
                .Select(n => new GetDoctorsBlockDaysResponseDto
                {
                    Id = n.Id,
                    DoctorId = n.doctorId,
                    DoctorName = $"{n.Doctor.user.firstName} {n.Doctor.user.lastName}",
                    date = date,
                    TimeSlot = n.timeSlot
                });
            return await query.ToPagedResultAsync(request.PageNumber, request.PageSize);
        }

        public async Task<Application.Common.PagedResult<GetDoctorsBlockDaysResponseDto>> DoctorGetBlokDays(int centerId, int userId, DateOnly date, PagedRequest request)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId && c.CenterStatus == CenterStatus.Active);
            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var doctor = await db.Doctors
                .Include(u=>u.user)
                .FirstOrDefaultAsync(u => u.userId == userId
                                     && u.user.isActive);

            if (doctor == null)
                throw new BusinessException("User.NotFound");

            var query = db.DoctorBlockedDays
                .Where(d => d.date == date
                       && d.doctorId == doctor.Id)
                .OrderByDescending(d => d.CreatedAt)
                .Select(n => new GetDoctorsBlockDaysResponseDto
                {
                    Id = n.Id,
                    DoctorId = n.doctorId,
                    DoctorName = $"{doctor.user.firstName} {doctor.user.lastName}",
                    date = date,
                    TimeSlot = n.timeSlot
                });
            return await query.ToPagedResultAsync(request.PageNumber, request.PageSize);
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
