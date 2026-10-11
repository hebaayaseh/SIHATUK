using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.Common;
using Sehatak.Application.Interfaces.INotification;
using Sehatak.Infrastructure.Services.NotificationService;
using System.Security.Claims;

namespace Sehatak.API.Controllers.NotificationController
{
    [ApiController]
    [Route("[Controller]")]
    public class NotificationController : ControllerBase
    {
        private readonly INotification notification;
        public NotificationController(INotification notification)
        {
            this.notification = notification;
        }

        [Authorize]
        [HttpGet("get-notifications/{centerId}")]
        public async Task<IActionResult> GetNotifications(int centerId , [FromQuery] PagedRequest request)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await notification.GetNotificatiosAsync(centerId, userId, request);
            return Ok(result);
        }
        [Authorize]
        [HttpPut("read-notifications/{centerId}/{notificationId}")]
        public async Task<IActionResult> GetNotifications(int centerId,int notificationId)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await notification.MarkNotificationReadAsync(centerId, userId, notificationId);
            return Ok(result);
        }

        [Authorize]
        [HttpPut("register-device-token/{centerId}")]
        public async Task<IActionResult> RegisterDeviceToken(int centerId, [FromBody] string token)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            await notification.RegisterDeviceTokenAsync(centerId, userId, token);
            return Ok();
        }
    }
}
