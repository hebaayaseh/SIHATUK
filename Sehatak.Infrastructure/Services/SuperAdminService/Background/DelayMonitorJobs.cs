using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.PaymentEnums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;

namespace Sehatak.Infrastructure.Services.SuperAdminService.Background
{
    public class DelayMonitorJobs : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<DelayMonitorJobs> _logger;

        public DelayMonitorJobs(IServiceScopeFactory scopeFactory, ILogger<DelayMonitorJobs> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await CheckSlotTime();
                await PrePayment();
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }

        
        private async Task PrePayment()
        {
            using var scope = _scopeFactory.CreateScope();
            var sharedDb = scope.ServiceProvider.GetRequiredService<SharedDbContext>();
            var contextFactory = scope.ServiceProvider.GetRequiredService<TenantDbContextFactory>();
            //var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
            //var whatsAppService = scope.ServiceProvider.GetRequiredService<IWhatsAppService>();

            var activeCenters = await sharedDb.MedicalCenters
                .Where(c => c.CenterStatus == CenterStatus.Active && c.RequiresPrepayment)
                .ToListAsync();

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var now = DateTime.UtcNow;

            foreach (var center in activeCenters)
            {
                try
                {
                    using var db = contextFactory.CreateForCenter(center.Id);

                    var expired = await db.Appointments
                        .Include(a => a.Payment)
                        .Include(a => a.Patient).ThenInclude(p => p.user)
                        .Where(a => a.appointmentDate == today
                            && a.appointmentStatus == AppointmentStatus.Pending
                            && a.Payment != null
                            && a.Payment.Status == PaymentStatus.Pending
                            && a.Payment.ExpiresAt.HasValue
                            && a.Payment.ExpiresAt.Value <= now)
                        .ToListAsync();

                    foreach (var appointment in expired)
                    {
                        appointment.appointmentStatus = AppointmentStatus.Cancelled;
                        appointment.Payment.Status = PaymentStatus.Failed;

                        db.Notifications.Add(new Notification
                        {
                            UserId = (int)appointment.Patient.userId,
                            Message = "انتهت مهلة الدفع المسبق لموعدك، تم إلغاء الحجز",
                            CreatedAt = DateTime.UtcNow,
                            Type = NotificationType.Cancellation,
                            IsRead = false
                        });

                        //await NotifyPatientAsync(notificationService, whatsAppService,
                        //    appointment.Patient.user, NotificationType.Cancellation,
                        //    "انتهت مهلة الدفع المسبق لموعدك، تم إلغاء الحجز.");

                        var nextWaiting = await db.Waitlists
                            .Include(w => w.Patient).ThenInclude(p => p.user)
                            .Where(w => w.DoctorId == appointment.doctorId && w.Status == WaitlistStatus.Waiting)
                            .OrderBy(w => w.CreatedAt)
                            .FirstOrDefaultAsync();

                        if (nextWaiting == null) continue;

                        var newAppointment = new Appointment
                        {
                            patientId = nextWaiting.PatientId,
                            doctorId = appointment.doctorId,
                            appointmentDate = today,
                            timeSlot = nextWaiting.PreferredTimeSlot.HasValue
                                    ? nextWaiting.PreferredTimeSlot.Value
                                    : null,
                            appointmentStatus = AppointmentStatus.Pending,
                        };
                        db.Appointments.Add(newAppointment);
                        await db.SaveChangesAsync(); 

                       
                        var newPayment = new Payment
                        {
                            PatientId = nextWaiting.PatientId,
                            AppointmentId = newAppointment.Id,
                            Amount = center.PrepaymentAmount, 
                            Status = PaymentStatus.Pending,
                            ExpiresAt = DateTime.UtcNow.AddMinutes(20),
                            Type = PaymentType.Prepayment
                        };

                        db.Payments.Add(newPayment);

                        nextWaiting.Status = WaitlistStatus.Entered;
                        nextWaiting.PromotedAppointmentId = newAppointment.Id;

                        db.Notifications.Add(new Notification
                        {
                            UserId = (int)nextWaiting.Patient.userId,
                            Message = "توفر لك موعد من قائمة الانتظار! يرجى إتمام الدفع خلال 20 دقيقة لتأكيد الحجز",
                            CreatedAt = DateTime.UtcNow,
                            Type = NotificationType.Cancellation,
                            IsRead = false
                        });
                        //await NotifyPatientAsync(notificationService, whatsAppService,
                        //    nextWaiting.Patient.user, NotificationType.Appointment,
                        //    "توفر لك موعد من قائمة الانتظار! يرجى إتمام الدفع خلال 20 دقيقة لتأكيد الحجز.");

                        await db.SaveChangesAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Prepayment timeout check failed for center {CenterId}", center.Id);
                    continue;
                }
            }
        }


        private async Task CheckSlotTime()
        {
            using var scope = _scopeFactory.CreateScope();
            var sharedDb = scope.ServiceProvider.GetRequiredService<SharedDbContext>();
            var contextFactory = scope.ServiceProvider.GetRequiredService<TenantDbContextFactory>();
            //var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
           // var whatsAppService = scope.ServiceProvider.GetRequiredService<IWhatsAppService>();

            var activeCenters = await sharedDb.MedicalCenters
                .Where(c => c.CenterStatus == CenterStatus.Active)
                .ToListAsync();

            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            foreach (var center in activeCenters)
            {
                try
                {
                    using var db = contextFactory.CreateForCenter(center.Id);

                    var completedToday = await db.Appointments
                        .Where(a => a.appointmentDate == today
                            && a.appointmentStatus == AppointmentStatus.Completed
                            && a.actualStartTime.HasValue
                            && a.actualEndTime.HasValue
                            && !a.DelayProcessed)
                        .ToListAsync();

                    foreach (var doctorGroup in completedToday.GroupBy(a => a.doctorId))
                    {
                        var doctorId = doctorGroup.Key;
                        var schedule = await db.DoctorSchedules
                            .FirstOrDefaultAsync(s => s.DoctorId == doctorId
                                                 && s.IsActive
                                                 && s.DayOfWeek == today.DayOfWeek);

                        if (schedule == null) continue;

                        double accumulatedDelay = 0;
                        foreach (var appt in doctorGroup)
                        {
                            var actualDuration = (appt.actualEndTime!.Value - appt.actualStartTime!.Value).TotalMinutes;
                            var extraDelay = actualDuration - schedule.SlotDurationMinutes;
                            if (extraDelay > 0)
                                accumulatedDelay += (double)extraDelay;
                            appt.DelayProcessed = true;
                        }
                        var lastBookedSlot = await db.Appointments
                            .Where(a => a.doctorId == doctorId 
                                   && a.appointmentDate == today 
                                   && a.timeSlot.HasValue)
                            .OrderByDescending(a => a.timeSlot)
                            .Select(a => a.timeSlot!.Value)
                            .FirstOrDefaultAsync();

                        var nextSlotTime = lastBookedSlot != default
                            ? lastBookedSlot.AddMinutes((double)schedule.SlotDurationMinutes)
                            : schedule.EndTime;

                        while (accumulatedDelay >= schedule.SlotDurationMinutes)
                        {
                            var nextWaiting = await db.Waitlists
                                .Include(w => w.Patient)
                                .ThenInclude(p => p.user)
                                .Where(w => w.DoctorId == doctorId 
                                       && w.Status == WaitlistStatus.Waiting)
                                .OrderBy(w => w.CreatedAt)
                                .FirstOrDefaultAsync();

                            if (nextWaiting == null) break;

                            var newAppointment = new Appointment
                            {
                                patientId = nextWaiting.PatientId,
                                doctorId = doctorId,
                                appointmentDate = today,
                                timeSlot = nextSlotTime,
                                appointmentStatus = AppointmentStatus.Confirmed,
                                createdAt = DateTime.UtcNow,
                                updateAt = DateTime.UtcNow
                            };
                            db.Appointments.Add(newAppointment);
                            await db.SaveChangesAsync();


                            if (center.RequiresPrepayment)
                            {
                                db.Payments.Add(new Payment
                                {
                                    PatientId = nextWaiting.PatientId,
                                    AppointmentId = newAppointment.Id,
                                    Amount = center.PrepaymentAmount,
                                    Status = PaymentStatus.Pending,
                                    ExpiresAt = DateTime.UtcNow.AddMinutes(20),
                                    Type = PaymentType.Prepayment
                                });
                            }

                            nextWaiting.Status = WaitlistStatus.Entered;
                            nextWaiting.PromotedAppointmentId = newAppointment.Id;

                            var message = center.RequiresPrepayment
                             ? "توفر لك موعد اليوم، يرجى إتمام الدفع خلال 20 دقيقة"
                             :"توفر لك موعد اليوم، تم تأكيد حجزك مباشرة.";

                            db.Notifications.Add(new Notification
                            {
                                UserId = (int)nextWaiting.Patient.userId,
                                Message = message,
                                CreatedAt = DateTime.UtcNow,
                                Type = NotificationType.Appointment,
                                IsRead = false
                            });

                            //await NotifyPatientAsync(notificationService, whatsAppService,
                            //    nextWaiting.Patient.user, NotificationType.Appointment, message);

                            accumulatedDelay -= (double)schedule.SlotDurationMinutes;
                        }
                    }

                    await db.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Delay monitor failed for center {CenterId}", center.Id);
                    continue;
                }
            }
        }

        
        //private async Task NotifyPatientAsync(INotificationService notificationService,
        //    IWhatsAppService whatsAppService,
        //    User user,
        //    NotificationType type,
        //    string message)
        //{
        //    await notificationService.CreateAsync(user.Id, type, message);

        //    if (!string.IsNullOrWhiteSpace(user.phoneNumber))
        //        await whatsAppService.SendMessageAsync(user.phoneNumber, message);
        //}
    }
}