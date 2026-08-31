using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.AppointmentDto;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.Interfaces.ApointmentInterface;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.PostponeEnums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.CalculateSlot;
using Sehatak.Infrastructure.Data;

namespace Sehatak.Infrastructure.Services.AppointmentService
{
    public class appointmentService : IAppointment
    {
        private readonly SharedDbContext sharedDbContext;
        private readonly TenantDbContextFactory contextFactory;
        private readonly GenerateTheoreticalSlots generateTheoreticalSlots;
        public appointmentService(SharedDbContext sharedDbContext , TenantDbContextFactory contextFactory , GenerateTheoreticalSlots generateTheoreticalSlot)
        {
            this.sharedDbContext = sharedDbContext;
            this.contextFactory = contextFactory;
            this.generateTheoreticalSlots = generateTheoreticalSlot;
        }

        public async Task<AvailableDoctorSlot> GetAvailableDoctorSlot(int centerId, int doctorId, DateOnly date)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (date < today)
                throw new BusinessException("Date.Invalid");

            var center = await sharedDbContext.MedicalCenters
               .FirstOrDefaultAsync(c => c.Id == centerId && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");


            using var db = contextFactory.CreateForCenter(centerId);

            var doctor = await db.Doctors
                .Include(u => u.user)
                .FirstOrDefaultAsync(d => d.Id == doctorId
                                     && d.user.isActive);

            if (doctor == null)
                throw new BusinessException("Doctor.NotFound");


            var isDayBlocked = await db.DoctorBlockedDays
                 .Where(bd => bd.doctorId == doctorId 
                        && bd.isBlocked 
                        && bd.date == date 
                        && bd.timeSlot.HasValue)
                 .Select(bd => bd.timeSlot!.Value)
                 .ToListAsync();


            var schedule = await db.DoctorSchedules
                .Include(d => d.doctor)
                .Where(d => d.DoctorId == doctorId
                       && d.IsActive
                       && d.DayOfWeek == date.DayOfWeek)
                .FirstOrDefaultAsync();

            if (schedule == null)
                throw new BusinessException("Doctor.NotFound");

            var theoreticalSlots = generateTheoreticalSlots.GenerateTheoreticalSlot(schedule.StartTime, schedule.EndTime, (int)schedule.SlotDurationMinutes);

            var bookedSlots = await db.Appointments
                .Where(a => a.doctorId == doctorId
                       && a.appointmentStatus == AppointmentStatus.Confirmed
                       && a.appointmentDate == date)
                       .Select(a => a.timeSlot)
                       .ToListAsync();

            var isWholeDayBlocked = await db.DoctorBlockedDays
                .AnyAsync(bd => bd.doctorId == doctorId && bd.isBlocked && bd.date == date && bd.timeSlot == null);
            var availableSlots = isWholeDayBlocked
                ? new List<TimeOnly>()
               :theoreticalSlots
               .Where(slot => slot.HasValue)
               .Select(slot => slot!.Value)
               .Except(bookedSlots.Where(b => b.HasValue).Select(b => b!.Value))
               .Except(isDayBlocked)
               .OrderBy(s => s)
               .ToList();

            if (date == today)
            {
                var nowTime = TimeOnly.FromDateTime(DateTime.UtcNow);
                availableSlots = availableSlots.Where(slot => slot > nowTime).ToList();
            }

           availableSlots = availableSlots.OrderBy(s => s).ToList();

            return new AvailableDoctorSlot
            {
                doctorId = doctorId,
                doctorName = $"{doctor.user.firstName} {doctor.user.lastName}",
                DayOfWeek = schedule.DayOfWeek,
                date = date,
                dateAvailable = availableSlots
            };

        }
        public async Task<BookAppointmentRespesponse> BookAppointmentAsync(int centerId , int doctorId ,int userId , BookAppointmentRequest request)
        {
            
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (request.dateOnly < today)
                throw new BusinessException("Date.Invalid");

            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var doctor = await db.Doctors
               .Include(u => u.user)
               .FirstOrDefaultAsync(d => d.Id == doctorId
                                    && d.user.isActive);

            if (doctor == null)
                throw new BusinessException("Doctor.NotFound");

            var isDayBlocked = await db.DoctorBlockedDays
                .Where(bd => bd.doctorId == doctorId 
                       && bd.isBlocked 
                       && bd.date == request.dateOnly 
                       && bd.timeSlot.HasValue)
                .Select(bd => bd.timeSlot!.Value)
                .ToListAsync();


            var schedule = await db.DoctorSchedules
               .Include(d => d.doctor)
               .Where(d => d.DoctorId == doctorId
                      && d.IsActive
                      && d.DayOfWeek == request.dateOnly.DayOfWeek)
               .FirstOrDefaultAsync();

            if (schedule == null)
                throw new BusinessException("Doctor.NotFound");

            var patient = await db.Patients
                .Include(u => u.user)
                .FirstOrDefaultAsync(u => u.userId == userId && u.user.isActive);

            if (patient == null)
                throw new BusinessException("Patient.NotFound");

            var hasExistingAppointment = await db.Appointments
               .AnyAsync(a => a.patientId == patient.patientId
                        && a.doctorId == doctorId
                        && a.appointmentDate == request.dateOnly
                        && a.appointmentStatus == AppointmentStatus.Confirmed);
            if (hasExistingAppointment)
                throw new BusinessException("Appointment.AlreadyExists");

            var theoreticalSlots = generateTheoreticalSlots.GenerateTheoreticalSlot(schedule.StartTime, schedule.EndTime, (int)schedule.SlotDurationMinutes);

            var bookedSlots = await db.Appointments
                .Where(a => a.doctorId == doctorId
                       && a.appointmentStatus == AppointmentStatus.Confirmed
                       && a.appointmentDate == request.dateOnly)
                      .Select(a => a.timeSlot)
                      .ToListAsync();

            var isWholeDayBlocked = await db.DoctorBlockedDays
               .AnyAsync(bd => bd.doctorId == doctorId
                         && bd.isBlocked && bd.date == request.dateOnly 
                         && bd.timeSlot == null);

            if (isWholeDayBlocked)
                throw new BusinessException("Doctor.DayBlocked");

            var availableSlots = theoreticalSlots
                .Where(slot => slot.HasValue)
                .Select(slot => slot!.Value)
                .Except(bookedSlots.Where(b => b.HasValue).Select(b => b!.Value))
                .Except(isDayBlocked)
                .OrderBy(s => s)
                .ToList();

            if (request.dateOnly == today)
            {
                var nowTime = TimeOnly.FromDateTime(DateTime.UtcNow);
                availableSlots = availableSlots.Where(slot => slot > nowTime).ToList();
            }

            availableSlots = availableSlots.OrderBy(s => s).ToList();

            

            if (!availableSlots.Contains(request.timeSlot))
            {
                if (availableSlots.Any())
                {
                    return new BookAppointmentRespesponse
                    {
                        Message = "هذا الموعد محجوز من قبل , يمكنك اختيار موعد اخر !",
                        Success = false,
                        AlternativeSlots = availableSlots
                    };

                }
                else
                {

                    db.Waitlists.Add(new Waitlist
                    {
                        PatientId = patient.patientId,
                        DoctorId = doctorId,
                        PreferredDate = request.dateOnly,
                        Status = WaitlistStatus.Waiting,
                        CreatedAt = DateTime.UtcNow
                    });
                    await db.SaveChangesAsync();
                    return new BookAppointmentRespesponse
                    {
                        Message = "اليوم ممتلئ بالكامل , تم وضعك في قائمة الانتظار في حال توفر موعد سيتم اخبارك",
                        Success = false,
                        AlternativeSlots = null
                    };
                }
            }


            await db.Appointments.AddAsync(
                new Appointment
                {
                    patientId = patient.patientId,
                    timeSlot = request.timeSlot,
                    appointmentDate = request.dateOnly,
                    appointmentStatus = AppointmentStatus.Confirmed,
                    doctorId = doctorId,
                    IsEmergency = false,
                    updateAt = DateTime.UtcNow,
                    createdAt = DateTime.UtcNow

                });

            await db.SaveChangesAsync();

            return new BookAppointmentRespesponse
            {
                Message = "تم حجز الموعد بنجاح",
                Success = true,
                AlternativeSlots = null
            };


        }

        public async Task<string> DeleteDoctorSlotAsync(int centerId, int userId, DeleteDoctorSlotRequest request)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (request.date < today)
                throw new BusinessException("Date.Invalid");

            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId && c.CenterStatus == CenterStatus.Active);
            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var doctor = await db.Doctors
                .Include(u => u.user)
                .FirstOrDefaultAsync(d => d.userId == userId
                                     && d.user.isActive);
                

            if (doctor == null)
                throw new BusinessException("Doctor.NotFound");

            var schedule = await db.DoctorSchedules
                .FirstOrDefaultAsync(d => d.DoctorId == doctor.Id
                                     && d.IsActive
                                     && d.DayOfWeek == request.date.DayOfWeek);

            if (schedule == null)
                throw new BusinessException("Schedule.NotFound");

            var doctorId = doctor.Id;

            var alreadyBlocked = await db.DoctorBlockedDays
                .AnyAsync(d => d.doctorId == doctor.Id && d.date == request.date && d.timeSlot == request.timeSlot && d.isBlocked);
            if (alreadyBlocked)
                throw new BusinessException("Slot.AlreadyBlocked");

            var bookedSlot = await db.Appointments
                .Include(p=>p.Patient)
                .ThenInclude(u=>u.user)
                .Where(a=>a.doctorId == doctorId
                       && a.appointmentStatus == AppointmentStatus.Confirmed
                       && a.appointmentDate == request.date
                       && a.timeSlot == request.timeSlot
                ).FirstOrDefaultAsync();

            if(bookedSlot != null)
            {
                bookedSlot.appointmentStatus = AppointmentStatus.Cancelled;
                bookedSlot.cancellationReason = request.Reason ?? "تم حجب هذا الموعد من قبل الطبيب";

                await db.PostponedServices.AddAsync(
                    new PostponedService
                    {
                        PatientId = bookedSlot.patientId,
                        CreatedByUserId = userId,
                        Type = PostponeType.DoctorAppointment,
                        AppointmentId = bookedSlot.Id,
                        Reason = request.Reason ?? "تم إلغاء الموعد من قبل الطبيب.",
                        Status = PostponeStatus.Active,
                    });

                db.Notifications.Add(new Notification
                {
                    UserId = (int)bookedSlot.Patient.userId,
                    Message = "نأسف لإبلاغكم بإلغاء موعدكم من قبل الطبيب. يرجى حجز موعد جديد.",
                    CreatedAt = DateTime.UtcNow,
                    Type = NotificationType.Cancellation,
                    IsRead = false
                });
            }
            db.DoctorBlockedDays.Add(
            new DoctorBlockedDay
            {
                  doctorId = doctorId,
                  date = request.date,
                  isBlocked = true,
                  timeSlot = request.timeSlot,
                  Reason = request.Reason,
                  CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            return "تم الغاء الموعد المحدد في نجاح.";

        }

        public async Task<string> CancelAppointmentAsync(int centerId,int doctorId, int userId, CancelAppointmentRequest request)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var doctor = await db.Doctors
                .Include(u => u.user)
                .FirstOrDefaultAsync(d => d.Id == doctorId
                                     && d.user.isActive);

            if (doctor == null)
                throw new BusinessException("Doctor.NotFound");


            var patient = await db.Patients
                .Include(u => u.user)
                .FirstOrDefaultAsync(u => u.userId == userId 
                                     && u.user.isActive);
            if (patient == null)
                throw new BusinessException("Patient.NotFound");

            var appointment = await db.Appointments
                .Include(p => p.Patient)
                .ThenInclude(u => u.user)
                .Where(a => a.doctorId == doctorId
                       && a.patientId == patient.patientId
                       && a.appointmentStatus == AppointmentStatus.Confirmed
                       && a.appointmentDate == request.date
                       && a.timeSlot == request.timeSlot
                ).FirstOrDefaultAsync();

            if (appointment == null)
                throw new BusinessException("Appointment.NotFound");

            appointment.appointmentStatus = AppointmentStatus.Cancelled;
            appointment.updateAt = DateTime.UtcNow;
            appointment.cancellationReason = request.Resone;

            await db.Notifications.AddAsync(new Notification
            {
                UserId = (int)appointment.Patient.userId,
                Type = NotificationType.Cancellation,
                IsRead = false,
                CreatedAt = DateTime.UtcNow,
                Message = "الغاء موعد."
            });

            var nextWaiting = await db.Waitlists
                    .Include(p => p.Patient)
                    .ThenInclude(u => u.user)
                    .Where(w => w.DoctorId == doctorId
                           && w.PreferredDate == request.date
                           && w.Status == WaitlistStatus.Waiting)
                    .OrderBy(w => w.CreatedAt)
                    .FirstOrDefaultAsync();

            if (nextWaiting != null)
            {
                await db.Appointments.AddAsync(new Appointment
                {
                    patientId = nextWaiting.PatientId,
                    appointmentDate = request.date,
                    timeSlot = request.timeSlot,
                    appointmentStatus = AppointmentStatus.Confirmed,
                    doctorId = doctorId,
                    createdAt = DateTime.UtcNow,
                    updateAt = DateTime.UtcNow
                });
                nextWaiting.Status = WaitlistStatus.Entered;


                await db.Notifications.AddAsync(new Notification
                {
                    UserId = (int)nextWaiting.Patient.userId,
                    Type = NotificationType.Appointment,
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow,
                    Message = $"توفر لك موعد في تاريخ {request.date} في الوقت {request.timeSlot}"
                });
            }
            await db.SaveChangesAsync();

            return "تم الغاء موعدك بنجاح";
        }

        public async Task<BookAppointmentRespesponse> RescheduleAppointmentAsync(int centerId, int doctorId, int userId, RescheduleAppointmentRequest request)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (request.date < today)
                throw new BusinessException("Date.Invalid");

            var center = await sharedDbContext.MedicalCenters
                .Where(c => c.Id == centerId && c.CenterStatus == CenterStatus.Active)
                .FirstOrDefaultAsync();

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var patient = await db.Patients
               .Include(p => p.user)
               .FirstOrDefaultAsync(p => p.userId == userId 
                                    && p.user.isActive);
            if (patient == null)
                throw new BusinessException("Patient.NotFound");



            var doctor = await db.Doctors
                 .Include(u => u.user)
                 .FirstOrDefaultAsync(d => d.Id == doctorId
                                      && d.user.isActive);

            if (doctor == null)
                throw new BusinessException("Doctor.NotFound");

            var appintment = await db.Appointments
                .Include(p=>p.Patient)
                .ThenInclude(u=>u.user)
                .Where(a => a.Id == request.appointmentId
                       && a.doctorId == doctor.Id
                       && a.patientId == patient.patientId
                       && a.appointmentStatus == AppointmentStatus.Confirmed)
                .FirstOrDefaultAsync();

            if (appintment == null)
                throw new BusinessException("Appointment.NotFound");

            var isWholeDayBlocked = await db.DoctorBlockedDays
                .AnyAsync(bd => bd.doctorId == doctorId 
                          && bd.isBlocked 
                          && bd.date == request.date 
                          && bd.timeSlot == null);

            if (isWholeDayBlocked)
                throw new BusinessException("Doctor.DayBlocked");

            var schedule = await db.DoctorSchedules
               .Include(d => d.doctor)
               .Where(d => d.DoctorId == doctorId
                      && d.IsActive
                      && d.DayOfWeek == request.date.DayOfWeek)
               .FirstOrDefaultAsync();

            if (schedule == null)
                throw new BusinessException("Doctor.NotFound");

            var theoreticalSlots = generateTheoreticalSlots.GenerateTheoreticalSlot(schedule.StartTime, schedule.EndTime, (int)schedule.SlotDurationMinutes);

            var bookedSlots = await db.Appointments
                 .Where(a => a.doctorId == doctorId
                 && a.appointmentStatus == AppointmentStatus.Confirmed
                 && a.appointmentDate == request.date)
                .Select(a => a.timeSlot)
                .ToListAsync();

            var blockedSlots = await db.DoctorBlockedDays
                .Where(bd => bd.doctorId == doctorId
                 && bd.isBlocked
                 && bd.date == request.date
                 && bd.timeSlot.HasValue)
                .Select(bd => bd.timeSlot!.Value)
                .ToListAsync();

            var availableSlots = theoreticalSlots
                .Where(slot => slot.HasValue)
                .Select(slot => slot!.Value)
                .Except(bookedSlots.Where(b => b.HasValue).Select(b => b!.Value))
                .Except(blockedSlots)
                .ToList();

            if (request.date == today)
            {
                var nowTime = TimeOnly.FromDateTime(DateTime.UtcNow);
                availableSlots = availableSlots.Where(slot => slot > nowTime).ToList();
            }

            availableSlots = availableSlots.OrderBy(s => s).ToList();

            if (!availableSlots.Contains(request.timeSlot))
            {

                return new BookAppointmentRespesponse
                {
                    Message = availableSlots.Any()
                        ? "هذا الموعد محجوز، يمكنك اختيار موعد آخر!"
                        : "لا يوجد مواعيد متاحة لهذا اليوم، يمكنك البقاء في موعدك الحالي.",
                    Success = false,
                    AlternativeSlots = availableSlots.Any() ? availableSlots : null
                };

            }
            if (appintment.RescheduleCount >= 3)
            {
                return new BookAppointmentRespesponse
                {
                    Message = "لقد تجاوزت العدد المسموح به لإعادة جدولة موعدك!",
                    Success = false,
                    AlternativeSlots = null
                };
            }

            var oldDate = appintment.appointmentDate;
            var oldTimeSlot = appintment.timeSlot;

            appintment.appointmentDate = request.date;
            appintment.timeSlot = request.timeSlot;
            appintment.RescheduleCount++;
            appintment.updateAt = DateTime.UtcNow;

            await db.Notifications.AddAsync(new Notification
            {
                UserId = (int)appintment.Patient.userId,
                IsRead = false,
                CreatedAt = DateTime.UtcNow,
                Message = "تم اعادة جدولة موعدك بنجاح.",
                Type = NotificationType.Appointment
            });

            await db.SaveChangesAsync();

            return new BookAppointmentRespesponse
            {
                Message = "تم اعادة جدولة موعدك بنجاح.",
                Success = true,
                AlternativeSlots = null
            };

        }

        public async Task<string> JoinWaitListAsync(int centerId, int doctorId, int userId, DateOnly date)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (date < today)
                throw new BusinessException("Date.Invalid");

            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var doctor = await db.Doctors
                .Include(u => u.user)
                .FirstOrDefaultAsync(d => d.Id == doctorId
                                     && d.user.isActive);

            if (doctor == null)
                throw new BusinessException("Doctor.NotFound");

            var patient = await db.Patients
                .Include(p => p.user)
                .FirstOrDefaultAsync(p => p.userId == userId
                                     && p.user.isActive);

            if (patient == null)
                throw new BusinessException("Patient.NotFound");

            var alreadyInWaitlist = await db.Waitlists
                .AnyAsync(w => w.DoctorId == doctorId
                       && w.PatientId == patient.patientId
                       && w.Status == WaitlistStatus.Waiting
                       && w.PreferredDate == date);
                

            if (alreadyInWaitlist)
                throw new BusinessException("Waitlist.AlreadyJoined");

            await db.Waitlists
                .AddAsync(new Waitlist
                {
                    PatientId = patient.patientId,
                    DoctorId = doctorId,
                    PreferredDate = date,
                    CreatedAt = DateTime.UtcNow,
                    Status = WaitlistStatus.Waiting,
                });

            

            await db.SaveChangesAsync();
            return "تم إضافتك لقائمة الانتظار، سيتم إعلامك عند توفر موعد.";

        }

        public async Task<List<GetPatientWaitList>> GetPatientsWaitListsAsync(int centerId, int doctorId,DateOnly date)
        {

            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var doctor = await db.Doctors
                .Include(u => u.user)
                .FirstOrDefaultAsync(d => d.Id == doctorId
                                     && d.user.isActive);

            if (doctor == null)
                throw new BusinessException("Doctor.NotFound");

            var patients = db.Waitlists
                .Include(p=>p.Patient)
                .Where(d => d.DoctorId == doctor.Id
                       && d.PreferredDate == date)
                .Select(w => new GetPatientWaitList
                {
                    WaitLisId = w.Id,
                    PatientId = w.PatientId,
                    PatientName = $"{w.Patient.user.firstName} {w.Patient.user.firstName}",
                    PhoneNumber = w.Patient.user.phoneNumber,
                    Email = w.Patient.user.email,
                    status = w.Status,
                    date = w.PreferredDate

                }).OrderBy(d=>d.date)
                .ToList();

            return patients;
        }

        public async Task<GetPatientWaitList> GetPatientWaitListsAsync(int centerId, int doctorId, int userId, DateOnly date)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var doctor = await db.Doctors
                .Include(u => u.user)
                .FirstOrDefaultAsync(d => d.Id == doctorId
                                     && d.user.isActive);

            if (doctor == null)
                throw new BusinessException("Doctor.NotFound");

            var patient = await db.Patients
                .Include(p => p.user)
                .FirstOrDefaultAsync(p => p.userId == userId
                                     && p.user.isActive);

            if (patient == null)
                throw new BusinessException("Patient.NotFound");

            var waitList =await db.Waitlists
                .Include(p => p.Patient)
                .Where(w => w.PatientId == patient.patientId
                                     && w.DoctorId == doctorId
                                     && w.PreferredDate == date)
                .Select(n => new GetPatientWaitList
                {
                    WaitLisId = n.Id,
                    PatientId = n.PatientId,
                    PatientName = $"{n.Patient.user.firstName} {n.Patient.user.lastName}",
                    PhoneNumber = n.Patient.user.phoneNumber,
                    date = n.PreferredDate,
                    Email = n.Patient.user.email,
                    status = n.Status
                }).FirstOrDefaultAsync();

            if (waitList == null)
                throw new BusinessException("WaitList.NotFound");

            return waitList;
        }
    }
}
