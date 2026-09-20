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
                try
                {
                    await CheckAndActivateSubscriptions();
                    await SendRenewalReminders();  
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in SubscriptionActivationService");
                }
                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
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

            foreach (var expired in expiredSubscriptions)
            {
                expired.Status = SubscriptionStatus.Expired;

                var center = await sharedDb.MedicalCenters.FindAsync(expired.CenterId);
                if (center != null)
                {
                    center.CenterStatus = CenterStatus.Suspended;

                    if (!string.IsNullOrEmpty(center.AdminEmail))
                    {
                        await emailService.SendCustomMessageAsync(
                            center.AdminEmail,
                            $"تعليق حساب {center.Name}",
                            $"تم تعليق حساب مركز {center.Name} بسبب انتهاء الاشتراك. يرجى إتمام الدفع لتفعيل الاشتراك الجديد إن وُجد، أو التواصل مع الإدارة."
                        );
                    }
                }

                _logger.LogWarning(
                    "Center {CenterId} suspended after subscription expiry", expired.CenterId);
            }

            await sharedDb.SaveChangesAsync();
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
                         && s.EndDate == reminderDate)
                .ToListAsync();

            foreach (var subscription in expiringSubscriptions)
            {
                if (subscription.Center?.AdminEmail != null)
                {
                    await emailService.SendSubscriptionRenewalReminderAsync(
                        subscription.Center.AdminEmail,
                        subscription.Center.Name,
                        subscription.EndDate
                    );

                    _logger.LogInformation(
                        "Renewal reminder sent to center {CenterId}", subscription.CenterId);
                }
            }
        }
    }
}
