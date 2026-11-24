using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sehatak.Application.Common;
using Sehatak.Application.Interfaces.IEmail;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;

namespace Sehatak.Infrastructure.Services.SuperAdminService.Background
{
    public class SubscriptionActivationService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<SubscriptionActivationService> _logger;

        public SubscriptionActivationService( IServiceScopeFactory scopeFactory,ILogger<SubscriptionActivationService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try { await CheckAndActivateSubscriptions(); }
                catch (Exception ex) { _logger.LogError(ex, "Subscription expiry pass failed"); }

                try { await SendRenewalReminders(); }
                catch (Exception ex) { _logger.LogError(ex, "Renewal reminder pass failed"); }

                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }

        private async Task CheckAndActivateSubscriptions()
        {
            using var scope = _scopeFactory.CreateScope();
            var sharedDb = scope.ServiceProvider.GetRequiredService<SharedDbContext>();
            var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

            var today = ClinicClock.Today;

            var expiredSubscriptions = await sharedDb.CenterSubscriptions
                .Include(s => s.Center)
                .Where(s => s.Status == SubscriptionStatus.Active && s.EndDate <= today)
                .ToListAsync();

            var toNotify = new List<(string Email, string Subject, string Message)>();
            foreach (var expired in expiredSubscriptions)
            {
                expired.Status = SubscriptionStatus.Expired;

                var center = await sharedDb.MedicalCenters.FindAsync(expired.CenterId);
                var hasAnotherActive = await sharedDb.CenterSubscriptions.AnyAsync(s =>
                    s.CenterId == expired.CenterId && s.Id != expired.Id
                    && s.Status == SubscriptionStatus.Active && s.EndDate > today);
                if (center != null && !hasAnotherActive)
                {
                    center.CenterStatus = CenterStatus.Suspended;

                    if (!string.IsNullOrEmpty(center.AdminEmail))
                    {
                        toNotify.Add((center.AdminEmail, $"تعليق حساب {center.Name}"," بسبب انتهاء الاشتراك. يرجى إتمام الدفع لتفعيل الاشتراك الجديد إن وُجد، أو التواصل مع الإدارة."));
                        
                    }
                }

                _logger.LogWarning(
                    "Center {CenterId} suspended after subscription expiry", expired.CenterId);
            }

            await sharedDb.SaveChangesAsync();
            foreach (var (email, subject, message) in toNotify)
            {
                try { await emailService.SendCustomMessageAsync(email, subject, message); }
                catch (Exception ex) { _logger.LogError(ex, "Suspension mail failed for {Email}", email); }
            }
        }
        
        private async Task SendRenewalReminders()
        {
            using var scope = _scopeFactory.CreateScope();
            var sharedDb = scope.ServiceProvider.GetRequiredService<SharedDbContext>();
            var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

            var today = ClinicClock.Today;
            var reminderDate = today.AddDays(7);


            var expiringSubscriptions = await sharedDb.CenterSubscriptions
    .Include(s => s.Center)
    .Where(s => s.Status == SubscriptionStatus.Active
             && s.EndDate > today
             && s.EndDate <= reminderDate
             && s.RenewalReminderSentAt == null)
    .ToListAsync();

            foreach (var subscription in expiringSubscriptions)
            {
                if (subscription.Center?.AdminEmail == null) continue;

                // نحجز التذكير أولاً: لو نسختين من التطبيق شغّالتين، واحدة بس بتبعت
                var claimed = await sharedDb.CenterSubscriptions
                    .Where(s => s.Id == subscription.Id && s.RenewalReminderSentAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.RenewalReminderSentAt, DateTime.UtcNow));
                if (claimed == 0) continue;

                try
                {
                    await emailService.SendSubscriptionRenewalReminderAsync(
                        subscription.Center.AdminEmail, subscription.Center.Name, subscription.EndDate);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Renewal reminder failed for center {CenterId}", subscription.CenterId);
                    await sharedDb.CenterSubscriptions.Where(s => s.Id == subscription.Id)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.RenewalReminderSentAt, (DateTime?)null));   // نعيد المحاولة بالدورة الجاية
                }
            }
        }
    }
}
