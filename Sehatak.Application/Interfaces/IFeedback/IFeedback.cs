using Sehatak.Application.Common;
using Sehatak.Application.DTOs.FeedbackDto;
using Sehatak.Domain.Enums.SharedEnums;

namespace Sehatak.Application.Interfaces.IFeedback
{
    public interface IFeedback
    {
        // ---- sender side ----
        Task<FeedbackResponseDto> SubmitAsync(int centerId, int userId, string role, string name, string email, SubmitFeedbackRequestDto request);
        Task<FeedbackResponseDto> SubmitGuestAsync(SubmitGuestFeedbackRequestDto request);
        Task<PagedResult<FeedbackResponseDto>> GetMyFeedbacksAsync(int centerId, int userId, PagedRequest request);

        // ---- SuperAdmin side ----
        Task<PagedResult<AdminFeedbackResponseDto>> GetFeedbacksAsync(PagedRequest request, FeedbackStatus? status, FeedbackType? type, int? centerId);
        Task<AdminFeedbackResponseDto> UpdateStatusAsync(int feedbackId, FeedbackStatus status);
        Task<AdminFeedbackResponseDto> ReplyAsync(int feedbackId, int superAdminId, ReplyFeedbackRequestDto request);

        Task<PagedResult<SuperAdminNotificationResponseDto>> GetSuperAdminNotificationsAsync(int superAdminId, PagedRequest request);
        Task<int> GetUnreadSuperAdminNotificationsCountAsync(int superAdminId);
        Task MarkSuperAdminNotificationReadAsync(int superAdminId, int notificationId);
        Task MarkAllSuperAdminNotificationsReadAsync(int superAdminId);
    }
}
