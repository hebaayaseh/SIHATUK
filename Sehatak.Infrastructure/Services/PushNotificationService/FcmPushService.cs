using FirebaseAdmin.Messaging;
using Microsoft.Extensions.Logging;
using Sehatak.Application.Interfaces.IPushNotification;
using FcmNotification = FirebaseAdmin.Messaging.Notification;

namespace Sehatak.Infrastructure.Services.PushNotificationService
{
    public class FcmPushService : IPushNotification
    {
        private readonly ILogger<FcmPushService> logger;

        public FcmPushService(ILogger<FcmPushService> logger)
        {
            this.logger = logger;
        }

        public async Task<bool> SendAsync(string token, string title, string body, IDictionary<string, string>? data = null)
        {
            if (string.IsNullOrWhiteSpace(token))
                return false;

            var message = new Message
            {
                Token = token,
                Notification = new FcmNotification { Title = title, Body = body },
                Data = data == null ? null : new Dictionary<string, string>(data),
            };

            try
            {
                var messaging = FirebaseMessaging.DefaultInstance;
                if (messaging == null)
                {
                    logger.LogWarning("Firebase is not configured; push skipped");
                    return false;
                }
                await messaging.SendAsync(message);
                return true;
            }
            catch (FirebaseMessagingException ex)
            {
                logger.LogWarning(ex, "FCM send failed for token {Token}: {Code}", token, ex.MessagingErrorCode);
                return false;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Push failed unexpectedly");
                return false;
            }
        }
    }
}