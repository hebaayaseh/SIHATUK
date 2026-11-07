using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sehatak.Application.Common;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.PaymentEnums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;

namespace Sehatak.Infrastructure.Services.SuperAdminService.Background
{
    /// <summary>
    /// Two jobs that run every minute:
    ///  1) PrePayment    : a booking whose prepayment was not paid in time is cancelled and its slot goes to the waiting list
    ///                     (the waiting list of the SAME day as the cancelled appointment).
    ///  2) CheckSlotTime : time the doctor saves during the day (patients who leave early, patients who did not come)
    ///                     is added up; every time it reaches one slot, one waiting-list patient of today gets a new slot at the end.
    /// </summary>
    public class DelayMonitorJobs : BackgroundService
    {
        /// <summary>How long a waiting-list patient has to pay the prepayment.</summary>
        private const int PrepaymentWindowMinutes = 20;

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
                // Each pass has its own try/catch: a failure must never stop the job (in .NET 8 it would stop the whole API).
                try { await CheckSlotTime(); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                { _logger.LogError(ex, "CheckSlotTime pass failed"); }

                try { await PrePayment(); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                { _logger.LogError(ex, "PrePayment pass failed"); }

                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }

        // =====================================================================================
        // 1) Prepayment that was not paid in time
        // =====================================================================================
        private async Task PrePayment()
        {
            using var scope = _scopeFactory.CreateScope();
            var sharedDb = scope.ServiceProvider.GetRequiredService<SharedDbContext>();
            var contextFactory = scope.ServiceProvider.GetRequiredService<TenantDbContextFactory>();

            var activeCenters = await sharedDb.MedicalCenters
                .Where(c => c.CenterStatus == CenterStatus.Active && c.RequiresPrepayment)
                .ToListAsync();

            var today = ClinicClock.Today;
            var nowUtc = DateTime.UtcNow;            // Payment.ExpiresAt is a UTC instant: compare it with UTC, never with clinic time

            foreach (var center in activeCenters)
            {
                try
                {
                    using var db = contextFactory.CreateForCenter(center.Id);

                    var expired = await db.Appointments
                        .AsNoTracking()
                        .Where(a => a.appointmentStatus == AppointmentStatus.Pending
                            && a.Payment != null
                            && a.Payment.Status == PaymentStatus.Pending
                            && a.Payment.ExpiresAt.HasValue
                            && a.Payment.ExpiresAt.Value <= nowUtc)
                        .Select(a => new
                        {
                            a.Id,
                            a.doctorId,
                            a.appointmentDate,
                            a.timeSlot,
                            PaymentId = a.Payment!.Id,
                            NotifyUserId = a.Patient.NotifiableUserId
                        })
                        .ToListAsync();

                    foreach (var expiredAppointment in expired)
                    {
                        try
                        {
                            await using var tx = await db.Database.BeginTransactionAsync();

                            // Claim the payment first: if the patient paid in the last second this updates 0 rows and we leave everything alone.
                            var claimed = await db.Payments
                                .Where(p => p.Id == expiredAppointment.PaymentId && p.Status == PaymentStatus.Pending)
                                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, PaymentStatus.Failed));
                            if (claimed == 0)
                                continue;

                            await db.Appointments
                                .Where(a => a.Id == expiredAppointment.Id && a.appointmentStatus == AppointmentStatus.Pending)
                                .ExecuteUpdateAsync(s => s
                                    .SetProperty(a => a.appointmentStatus, AppointmentStatus.Cancelled)
                                    .SetProperty(a => a.updateAt, DateTime.UtcNow));

                            db.Notifications.Add(new Notification
                            {
                                UserId = expiredAppointment.NotifyUserId,
                                Message = "انتهت مهلة الدفع المسبق لموعدك، تم إلغاء الحجز",
                                CreatedAt = DateTime.UtcNow,
                                Type = NotificationType.Cancellation,
                                IsRead = false
                            });

                            // The freed slot goes to the first patient waiting for THE SAME DAY as the cancelled appointment.
                            if (expiredAppointment.appointmentDate >= today)
                            {
                                var nextWaiting = await db.Waitlists
                                    .Include(w => w.Patient)
                                    .Where(w => w.DoctorId == expiredAppointment.doctorId
                                             && w.PreferredDate == expiredAppointment.appointmentDate
                                             && w.Status == WaitlistStatus.Waiting)
                                    .OrderBy(w => w.CreatedAt)
                                    .FirstOrDefaultAsync();

                                if (nextWaiting != null)
                                {
                                    var newAppointment = new Appointment
                                    {
                                        patientId = nextWaiting.PatientId,
                                        doctorId = expiredAppointment.doctorId,
                                        appointmentDate = expiredAppointment.appointmentDate,
                                        timeSlot = expiredAppointment.timeSlot,          // takes over the cancelled time
                                        appointmentStatus = AppointmentStatus.Pending
                                    };
                                    db.Appointments.Add(newAppointment);
                                    await db.SaveChangesAsync();

                                    db.Payments.Add(new Payment
                                    {
                                        PatientId = nextWaiting.PatientId,
                                        AppointmentId = newAppointment.Id,
                                        Amount = center.PrepaymentAmount,
                                        Status = PaymentStatus.Pending,
                                        ExpiresAt = DateTime.UtcNow.AddMinutes(PrepaymentWindowMinutes),
                                        Type = PaymentType.Prepayment
                                    });

                                    nextWaiting.Status = WaitlistStatus.Entered;
                                    nextWaiting.PromotedAppointmentId = newAppointment.Id;

                                    db.Notifications.Add(new Notification
                                    {
                                        UserId = nextWaiting.Patient.NotifiableUserId,
                                        Message = $"توفر لك موعد من قائمة الانتظار! يرجى إتمام الدفع خلال {PrepaymentWindowMinutes} دقيقة لتأكيد الحجز",
                                        CreatedAt = DateTime.UtcNow,
                                        Type = NotificationType.Waitlist,
                                        IsRead = false
                                    });
                                }
                            }

                            await db.SaveChangesAsync();          // always: the cancellation is saved even when nobody is waiting
                            await tx.CommitAsync();
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            // one bad appointment must not block the others of the same center
                            _logger.LogError(ex, "Prepayment timeout failed for appointment {AppointmentId} in center {CenterId}",
                                expiredAppointment.Id, center.Id);
                            db.ChangeTracker.Clear();
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Prepayment timeout check failed for center {CenterId}", center.Id);
                }
            }
        }

        // =====================================================================================
        // 2) Time saved during the day goes to the waiting list
        // =====================================================================================
        // For one doctor and one day we keep a "saved minutes" balance:
        //   + (slot - real duration)  for every finished visit      (even one minute early counts; running over subtracts)
        //   + a full slot             for every booked patient who did not come (status NoShow, or a confirmed appointment
        //                              whose slot has ended without the patient being checked in or started)
        //   - one slot                for every appointment this job already created for the waiting list
        // Whenever the balance reaches one slot and somebody is waiting for today, that person gets a slot at the end of the day.
        // The balance is recomputed from the day's appointments every time, so nothing is lost between runs and nothing is counted twice.
        private async Task CheckSlotTime()
        {
            using var scope = _scopeFactory.CreateScope();
            var sharedDb = scope.ServiceProvider.GetRequiredService<SharedDbContext>();
            var contextFactory = scope.ServiceProvider.GetRequiredService<TenantDbContextFactory>();

            var activeCenters = await sharedDb.MedicalCenters
                .Where(c => c.CenterStatus == CenterStatus.Active)
                .ToListAsync();

            var now = ClinicClock.Now;
            var today = ClinicClock.Today;
            var nowMinute = now.Hour * 60 + now.Minute;

            foreach (var center in activeCenters)
            {
                try
                {
                    using var db = contextFactory.CreateForCenter(center.Id);

                    // Only doctors with somebody waiting for today matter; the balance is rebuilt later when someone starts waiting.
                    var doctorIds = await db.Waitlists
                        .Where(w => w.Status == WaitlistStatus.Waiting && w.PreferredDate == today)
                        .Select(w => w.DoctorId)
                        .Distinct()
                        .ToListAsync();

                    foreach (var doctorId in doctorIds)
                    {
                        var schedule = await db.DoctorSchedules
                            .AsNoTracking()
                            .FirstOrDefaultAsync(s => s.DoctorId == doctorId
                                                 && s.IsActive
                                                 && s.DayOfWeek == today.DayOfWeek);

                        if (schedule == null || schedule.SlotDurationMinutes is not > 0)
                            continue;

                        var slot = schedule.SlotDurationMinutes.Value;

                        var day = await db.Appointments
                            .AsNoTracking()
                            .Where(a => a.doctorId == doctorId && a.appointmentDate == today)
                            .Select(a => new
                            {
                                a.appointmentStatus,
                                a.timeSlot,
                                a.actualStartTime,
                                a.actualEndTime,
                                a.CheckInTime,
                                a.IsTimeCompensation
                            })
                            .ToListAsync();

                        double balance = 0;
                        foreach (var a in day)
                        {
                            if (a.IsTimeCompensation)
                            {
                                if (a.appointmentStatus != AppointmentStatus.Cancelled)
                                    balance -= slot;                                   // a slot already given to the waiting list
                                if (a.appointmentStatus != AppointmentStatus.Completed)
                                    continue;                                          // its own early/late is only counted once it is finished
                            }

                            if (a.appointmentStatus == AppointmentStatus.Completed
                                && a.actualStartTime.HasValue && a.actualEndTime.HasValue)
                            {
                                balance += slot - (a.actualEndTime.Value - a.actualStartTime.Value).TotalMinutes;
                            }
                            else if (a.appointmentStatus == AppointmentStatus.NoShow)
                            {
                                balance += slot;
                            }
                            else if (a.appointmentStatus == AppointmentStatus.Confirmed
                                && a.actualStartTime == null
                                && a.CheckInTime == null
                                && a.timeSlot.HasValue
                                && a.timeSlot.Value.Hour * 60 + a.timeSlot.Value.Minute + slot <= nowMinute)
                            {
                                balance += slot;                                       // booked, slot is over, patient never came
                            }
                        }

                        if (balance < slot)
                            continue;

                        // first free time at the end of the day, never in the past
                        var lastBooked = day.Where(a => a.timeSlot.HasValue && a.appointmentStatus != AppointmentStatus.Cancelled)
                                            .Select(a => a.timeSlot!.Value)
                                            .DefaultIfEmpty(default)
                                            .Max();
                        var nextMinute = day.Any(a => a.timeSlot.HasValue && a.appointmentStatus != AppointmentStatus.Cancelled)
                            ? lastBooked.Hour * 60 + lastBooked.Minute + slot
                            : schedule.EndTime.Hour * 60 + schedule.EndTime.Minute;

                        var startMinute = schedule.StartTime.Hour * 60 + schedule.StartTime.Minute;
                        if (nowMinute > startMinute)
                        {
                            var nextGrid = startMinute + (int)Math.Ceiling((nowMinute - startMinute) / (double)slot) * slot;
                            if (nextMinute < nextGrid) nextMinute = nextGrid;
                        }

                        while (balance >= slot && nextMinute < 24 * 60)
                        {
                            await using var tx = await db.Database.BeginTransactionAsync();

                            var nextWaiting = await db.Waitlists
                                .Include(w => w.Patient)
                                .Where(w => w.DoctorId == doctorId
                                         && w.PreferredDate == today
                                         && w.Status == WaitlistStatus.Waiting)
                                .OrderBy(w => w.CreatedAt)
                                .FirstOrDefaultAsync();

                            if (nextWaiting == null) break;

                            var newAppointment = new Appointment
                            {
                                patientId = nextWaiting.PatientId,
                                doctorId = doctorId,
                                appointmentDate = today,
                                timeSlot = new TimeOnly(nextMinute / 60, nextMinute % 60),
                                // with prepayment the booking waits for the payment, like every other prepaid booking
                                appointmentStatus = center.RequiresPrepayment ? AppointmentStatus.Pending : AppointmentStatus.Confirmed,
                                IsTimeCompensation = true,
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
                                    ExpiresAt = DateTime.UtcNow.AddMinutes(PrepaymentWindowMinutes),
                                    Type = PaymentType.Prepayment
                                });
                            }

                            nextWaiting.Status = WaitlistStatus.Entered;
                            nextWaiting.PromotedAppointmentId = newAppointment.Id;

                            db.Notifications.Add(new Notification
                            {
                                UserId = nextWaiting.Patient.NotifiableUserId,
                                Message = center.RequiresPrepayment
                                    ? $"توفر لك موعد اليوم، يرجى إتمام الدفع خلال {PrepaymentWindowMinutes} دقيقة"
                                    : "توفر لك موعد اليوم، تم تأكيد حجزك مباشرة.",
                                CreatedAt = DateTime.UtcNow,
                                Type = NotificationType.Appointment,
                                IsRead = false
                            });

                            await db.SaveChangesAsync();
                            await tx.CommitAsync();

                            balance -= slot;
                            nextMinute += slot;                    // the next promoted patient gets the following slot
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Delay monitor failed for center {CenterId}", center.Id);
                }
            }
        }
    }
}