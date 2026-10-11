
using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.DTOs.NotificatiionDto;
using Sehatak.Application.Interfaces.INotification;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;

namespace Sehatak.Infrastructure.Services.NotificationService
{
    public class NotificationService : INotification
    {
        private readonly SharedDbContext sharedDbContext;
        private readonly TenantDbContextFactory contextFactory;
        public NotificationService(SharedDbContext sharedDbContext, TenantDbContextFactory contextFactory)
        {
            this.sharedDbContext = sharedDbContext;
            this.contextFactory = contextFactory;
        }

        public async Task<PagedResult<GetNotificatioResponseDto>> GetNotificatiosAsync(int centerId, int userId,PagedRequest request)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var user = await db.Users
                .FirstOrDefaultAsync(u => u.Id == userId
                                    && u.isActive);

            if (user == null)
                throw new BusinessException("User.NotFound");

            var query =  db.Notifications
                .Where(i => i.UserId == userId)
                .OrderByDescending(c => c.CreatedAt)
                .Select(n => new GetNotificatioResponseDto
                {
                    Type = n.Type.ToString(),
                    Message = n.Message,
                    CreatedAt = n.CreatedAt,
                    isRead = n.IsRead,
                    NotificationId = n.Id
                    
                });
            return await query.ToPagedResultAsync(request.PageNumber, request.PageSize);
        }

        public async Task<NotificationSummaryDto> MarkNotificationReadAsync(int centerId, int userId, int notificationId)
        {
            var center = await sharedDbContext.MedicalCenters
                            .FirstOrDefaultAsync(c => c.Id == centerId
                                                 && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var user = await db.Users
                .FirstOrDefaultAsync(u => u.Id == userId
                                    && u.isActive);

            if (user == null)
                throw new BusinessException("User.NotFound");

            var notification = await db.Notifications
                .FirstOrDefaultAsync(n=>n.Id ==  notificationId
                                     && n.UserId == userId);

            if (notification == null)
                throw new BusinessException("Notification.NotFound");
            notification.IsRead = true;
            await db.SaveChangesAsync();

            return  new NotificationSummaryDto
            {
                NotificationId = notification.Id,
                CreatedAt = notification.CreatedAt,
                isRead = notification.IsRead,
                Message = notification.Message,
                Type = notification.Type.ToString()
            };
        }

        public async Task RegisterDeviceTokenAsync(int centerId, int userId, string token)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.isActive);
            if (user == null)
                throw new BusinessException("User.NotFound");

            user.FcmToken = token;
            await db.SaveChangesAsync();
        }
    }
}
