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
    public class MedicationReminderJob : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<MedicationReminderJob> _logger;

        public MedicationReminderJob(IServiceScopeFactory scopeFactory, ILogger<MedicationReminderJob> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await CheckMedicationReminders();
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }

        private async Task CheckMedicationReminders()
        {
            using var scope = _scopeFactory.CreateScope();
            var sharedDb = scope.ServiceProvider.GetRequiredService<SharedDbContext>();
            var contextFactory = scope.ServiceProvider.GetRequiredService<TenantDbContextFactory>();

            var activeCenters = await sharedDb.MedicalCenters
                .Where(c => c.CenterStatus == CenterStatus.Active)
                .ToListAsync();

            var today = ClinicClock.Today;
            var now = ClinicClock.Now;
            var nowTime = TimeOnly.FromDateTime(now);
            var todayFlag = (ReminderDays)(1 << (int)today.DayOfWeek);

            foreach (var center in activeCenters)
            {
                try
                {
                    using var db = contextFactory.CreateForCenter(center.Id);

                    var dueReminders = await db.MedicationReminders
                        .Include(r => r.Patient)
                        .ThenInclude(p => p.user)
                        .Where(r => r.IsActive
                            && today >= r.StartDate
                            && today <= r.EndDate
                            && (r.Days & todayFlag) == todayFlag
                            && r.ReminderTime.Hour == nowTime.Hour
                            && r.ReminderTime.Minute == nowTime.Minute
                            && r.LastNotifiedDate != today)
                        .ToListAsync();

                    foreach (var reminder in dueReminders)
                    {
                        db.Notifications.Add(new Notification
                        {
                            UserId = reminder.Patient.NotifiableUserId,
                            Message = $"حان وقت أخذ دواء: {reminder.MedicationName}",
                            CreatedAt = DateTime.UtcNow,
                            Type = NotificationType.MedicationReminder,
                            IsRead = false
                        });

                        reminder.LastNotifiedDate = today;

                        if (today == reminder.EndDate)
                            reminder.IsActive = false;
                    }

                    await db.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Medication reminder check failed for center {CenterId}", center.Id);
                    continue;
                }
            }
        }
    }
}