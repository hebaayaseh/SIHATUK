

using Sehatak.Application.Common;
using Sehatak.Application.DTOs.NotificatiionDto;

namespace Sehatak.Application.Interfaces.INotification
{
    public interface INotification
    {
        Task<PagedResult<GetNotificatioResponseDto>> GetNotificatiosAsync(int centerId, int userId,PagedRequest request);
        Task<NotificationSummaryDto> MarkNotificationReadAsync(int centerId, int userId, int notificationId);
        Task RegisterDeviceTokenAsync(int centerId, int userId, string token);
    }
}
