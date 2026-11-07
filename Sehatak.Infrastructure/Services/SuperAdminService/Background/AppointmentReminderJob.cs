using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sehatak.Application.Common;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;

namespace Sehatak.Infrastructure.Services.SuperAdminService.Background
{
    /// <summary>
    /// يبعت للمريض تذكير قبل موعده بمقدار مدة السلوت عند الدكتور (SlotDurationMinutes)،
    /// وبحد أدنى 30 دقيقة. لو الدكتور ما إله جدول فعّال اليوم بنستخدم 30 دقيقة.
    ///
    /// لو المريض حجز (أو أعاد الجدولة) وهو أصلاً داخل نافذة التذكير، ما بنبعت تذكير
    /// لأنه لسا واصله إشعار الحجز، وبنعلّم الموعد ReminderSent = true عشان ما نعيد الفحص.
    ///
    /// بنستخدم نافذة (مش مطابقة دقيقة بالضبط) فلو السيرفر وقع دقايق ما بنضيّع التذكير،
    /// بس ما بنبعت تذكير لموعد فات وقته.
    /// </summary>
    public class AppointmentReminderJob : BackgroundService
    {
        private const int MinLeadMinutes = 30;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<AppointmentReminderJob> _logger;

        public AppointmentReminderJob(IServiceScopeFactory scopeFactory, ILogger<AppointmentReminderJob> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckAppointmentReminders();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Appointment reminder job failed");
                }

                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }

        private async Task CheckAppointmentReminders()
        {
            using var scope = _scopeFactory.CreateScope();
            var sharedDb = scope.ServiceProvider.GetRequiredService<SharedDbContext>();
            var contextFactory = scope.ServiceProvider.GetRequiredService<TenantDbContextFactory>();

            var activeCenters = await sharedDb.MedicalCenters
                .Where(c => c.CenterStatus == CenterStatus.Active)
                .ToListAsync();

            var now = ClinicClock.Now;
            var today = ClinicClock.Today;
            var nowTime = TimeOnly.FromDateTime(now);
            var nowUtc = DateTime.UtcNow;

            foreach (var center in activeCenters)
            {
                try
                {
                    using var db = contextFactory.CreateForCenter(center.Id);

                    // مدة السلوت لكل دكتور اليوم (دكتور ما إله جدول فعّال = الحد الأدنى)
                    var slotMinutesByDoctor = (await db.DoctorSchedules
                            .Where(s => s.IsActive
                                     && s.DayOfWeek == today.DayOfWeek
                                     && s.SlotDurationMinutes.HasValue)
                            .ToListAsync())
                        .GroupBy(s => s.DoctorId)
                        .ToDictionary(g => g.Key, g => g.First().SlotDurationMinutes!.Value);

                    var maxLead = Math.Max(MinLeadMinutes,
                        slotMinutesByDoctor.Values.DefaultIfEmpty(0).Max());

                    // أوسع نافذة ممكنة، والتفصيل لكل دكتور بنعمله بالذاكرة تحت
                    var limit = nowTime.AddMinutes(maxLead);
                    if (limit < nowTime)
                        limit = TimeOnly.MaxValue; // النافذة تعدّت منتصف الليل

                    var candidates = await db.Appointments
                        .Include(a => a.Patient)
                        .Where(a => a.appointmentDate == today
                            && a.appointmentStatus == AppointmentStatus.Confirmed
                            && !a.ReminderSent
                            && a.timeSlot.HasValue
                            && a.timeSlot.Value > nowTime
                            && a.timeSlot.Value <= limit)
                        .ToListAsync();

                    foreach (var appointment in candidates)
                    {
                        var slot = appointment.timeSlot!.Value;
                        var lead = Math.Max(MinLeadMinutes,
                            slotMinutesByDoctor.GetValueOrDefault(appointment.doctorId, MinLeadMinutes));

                        var minutesUntilSlot = (slot - nowTime).TotalMinutes;
                        if (minutesUntilSlot > lead)
                            continue; // لسا بدري على تذكير هالموعد

                        // لحظة آخر حجز: بعد إعادة الجدولة هي updateAt، وغير هيك هي createdAt
                        var bookedAt = appointment.RescheduleCount > 0
                            ? appointment.updateAt
                            : appointment.createdAt;
                        var minutesSinceBooked = (nowUtc - bookedAt).TotalMinutes;

                        // المريض حجز وهو داخل نافذة التذكير -> تجاهل، واصله إشعار الحجز للتو
                        if (minutesUntilSlot + minutesSinceBooked < lead)
                        {
                            appointment.ReminderSent = true;
                            continue;
                        }

                        db.Notifications.Add(new Notification
                        {
                            UserId = appointment.Patient.NotifiableUserId,
                            Message = $"تذكير: عندك موعد اليوم الساعة {slot:HH:mm}.",
                            CreatedAt = DateTime.UtcNow,
                            Type = NotificationType.Reminder,
                            IsRead = false
                        });

                        appointment.ReminderSent = true;
                    }

                    await db.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Appointment reminder check failed for center {CenterId}", center.Id);
                    continue;
                }
            }
        }
    }
}
