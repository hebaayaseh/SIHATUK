using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Sehatak.Application.Interfaces.IPushNotification;
using Sehatak.Domain.Entities.TenantEntities;

namespace Sehatak.Infrastructure.Data
{
    public class NotificationPushInterceptor : SaveChangesInterceptor
    {
        private readonly IPushNotification pushSender;
        private List<Notification> pendingNotifications = new();

        public NotificationPushInterceptor(IPushNotification pushSender)
        {
            this.pushSender = pushSender;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is not null)
            {
                pendingNotifications = eventData.Context.ChangeTracker
                    .Entries<Notification>()
                    .Where(e => e.State == EntityState.Added)
                    .Select(e => e.Entity)
                    .ToList();
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is TenantDbContext db && pendingNotifications.Count > 0)
            {
                var userIds = pendingNotifications.Select(n => n.UserId).Distinct().ToList();

                var tokens = await db.Users
                    .Where(u => userIds.Contains(u.Id) && u.FcmToken != null)
                    .ToDictionaryAsync(u => u.Id, u => u.FcmToken!, cancellationToken);

                foreach (var notification in pendingNotifications)
                {
                    if (tokens.TryGetValue(notification.UserId, out var token))
                    {
                        var ok = await pushSender.SendAsync(
                            token,
                            notification.Type.ToString(),
                            notification.Message,
                            new Dictionary<string, string>
                            {
                                ["notificationId"] = notification.Id.ToString(),
                                ["type"] = notification.Type.ToString()
                            });

                        if (!ok)
                        {
                            // التوكن تالف/منتهي — امسحه مشان ما نحاول نرسله مرة ثانية
                            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == notification.UserId, cancellationToken);
                            if (user != null)
                            {
                                user.FcmToken = null;
                                await db.SaveChangesAsync(cancellationToken);
                            }
                        }
                    }
                }

                pendingNotifications = new();
            }

            return await base.SavedChangesAsync(eventData, result, cancellationToken);
        }
    }
}