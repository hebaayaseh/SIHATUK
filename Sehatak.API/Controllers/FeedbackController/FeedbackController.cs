using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.FeedbackDto;
using Sehatak.Application.Interfaces.IFeedback;
using Sehatak.Domain.Enums.SharedEnums;
using System.Security.Claims;

namespace Sehatak.API.Controllers.FeedbackController
{
    [ApiController]
    [Route("[Controller]")]
    public class FeedbackController : ControllerBase
    {
        private readonly IFeedback feedback;
        public FeedbackController(IFeedback feedback)
        {
            this.feedback = feedback;
        }



        [Authorize]
        [HttpPost("submit-feedback/{centerId}")]
        public async Task<IActionResult> SubmitFeedback(int centerId, [FromBody] SubmitFeedbackRequestDto request)
        {
            var userId = GetTenantUserId();
            var role = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;
            var name = User.FindFirst(ClaimTypes.Name)?.Value ?? string.Empty;
            var email = User.FindFirst(ClaimTypes.Email)?.Value ?? string.Empty;

            var result = await feedback.SubmitAsync(centerId, userId, role, name, email, request);
            return Ok(result);
        }

        [Authorize]
        [HttpGet("get-my-feedbacks/{centerId}")]
        public async Task<IActionResult> GetMyFeedbacks(int centerId, [FromQuery] PagedRequest request)
        {
            var userId = GetTenantUserId();
            var result = await feedback.GetMyFeedbacksAsync(centerId, userId, request);
            return Ok(result);
        }



        [AllowAnonymous]
        [EnableRateLimiting("LoginPolicy")]
        [HttpPost("submit-guest-feedback")]
        public async Task<IActionResult> SubmitGuestFeedback([FromBody] SubmitGuestFeedbackRequestDto request)
        {
            var result = await feedback.SubmitGuestAsync(request);
            return Ok(result);
        }


        [Authorize(Policy = "SuperAdminOnly")]
        [HttpGet("superAdmin-get-feedbacks")]
        public async Task<IActionResult> GetFeedbacks(
            [FromQuery] PagedRequest request,
            [FromQuery] FeedbackStatus? status,
            [FromQuery] FeedbackType? type,
            [FromQuery] int? centerId)
        {
            var result = await feedback.GetFeedbacksAsync(request, status, type, centerId);
            return Ok(result);
        }

        [Authorize(Policy = "SuperAdminOnly")]
        [HttpPatch("superAdmin-update-feedback-status/{feedbackId}")]
        public async Task<IActionResult> UpdateFeedbackStatus(int feedbackId, [FromBody] UpdateFeedbackStatusRequestDto request)
        {
            var result = await feedback.UpdateStatusAsync(feedbackId, request.Status);
            return Ok(result);
        }

        [Authorize(Policy = "SuperAdminOnly")]
        [HttpPost("superAdmin-reply-feedback/{feedbackId}")]
        public async Task<IActionResult> ReplyToFeedback(int feedbackId, [FromBody] ReplyFeedbackRequestDto request)
        {
            var superAdminId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await feedback.ReplyAsync(feedbackId, superAdminId, request);
            return Ok(result);
        }

        [Authorize(Policy = "SuperAdminOnly")]
        [HttpGet("superAdmin-get-notifications")]
        public async Task<IActionResult> GetSuperAdminNotifications([FromQuery] PagedRequest request)
        {
            var superAdminId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await feedback.GetSuperAdminNotificationsAsync(superAdminId, request);
            return Ok(result);
        }

        [Authorize(Policy = "SuperAdminOnly")]
        [HttpGet("superAdmin-unread-notifications-count")]
        public async Task<IActionResult> GetUnreadNotificationsCount()
        {
            var superAdminId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var count = await feedback.GetUnreadSuperAdminNotificationsCountAsync(superAdminId);
            return Ok(new { unreadCount = count });
        }

        [Authorize(Policy = "SuperAdminOnly")]
        [HttpPut("superAdmin-read-notification/{notificationId}")]
        public async Task<IActionResult> MarkNotificationRead(int notificationId)
        {
            var superAdminId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            await feedback.MarkSuperAdminNotificationReadAsync(superAdminId, notificationId);
            return Ok(new { success = true });
        }

        [Authorize(Policy = "SuperAdminOnly")]
        [HttpPut("superAdmin-read-all-notifications")]
        public async Task<IActionResult> MarkAllNotificationsRead()
        {
            var superAdminId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            await feedback.MarkAllSuperAdminNotificationsReadAsync(superAdminId);
            return Ok(new { success = true });
        }

        private int GetTenantUserId()
        {
            if (User.FindFirst("CenterId") == null)
                throw new UnauthorizedAccessException();

            return int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        }
    }
}
