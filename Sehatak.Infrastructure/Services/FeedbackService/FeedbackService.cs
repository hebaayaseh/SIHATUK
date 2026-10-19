using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.DTOs.FeedbackDto;
using Sehatak.Application.Interfaces.IEmail;
using Sehatak.Application.Interfaces.IFeedback;
using Sehatak.Domain.Entities.SharedEntities;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;

namespace Sehatak.Infrastructure.Services.FeedbackService
{
    public class FeedbackService : IFeedback
    {
        private readonly SharedDbContext sharedDbContext;
        private readonly TenantDbContextFactory contextFactory;
        private readonly IEmailService emailService;
        private readonly ILogger<FeedbackService> logger;

        public FeedbackService(SharedDbContext sharedDbContext,TenantDbContextFactory contextFactory,IEmailService emailService,ILogger<FeedbackService> logger)
        {
            this.sharedDbContext = sharedDbContext;
            this.contextFactory = contextFactory;
            this.emailService = emailService;
            this.logger = logger;
        }

        public async Task<FeedbackResponseDto> SubmitAsync(int centerId, int userId, string role, string name, string email, SubmitFeedbackRequestDto request)
        {
            var centerExists = await sharedDbContext.MedicalCenters.AnyAsync(c => c.Id == centerId);
            if (!centerExists)
                throw new BusinessException("Center.NotFound");

            var feedback = new Feedback
            {
                CenterId = centerId,
                SenderUserId = userId,
                SenderRole = Truncate(role, 30),
                SenderName = Truncate(name, 200),
                SenderEmail = Truncate(email, 150),
                Type = request.Type,
                Subject = request.Subject.Trim(),
                Message = request.Message.Trim()
            };

            await SaveAndNotifySuperAdminsAsync(feedback);
            return MapToUserDto(feedback);
        }

        public async Task<FeedbackResponseDto> SubmitGuestAsync(SubmitGuestFeedbackRequestDto request)
        {
            if (request.CenterId.HasValue)
            {
                var centerExists = await sharedDbContext.MedicalCenters.AnyAsync(c => c.Id == request.CenterId.Value);
                if (!centerExists)
                    throw new BusinessException("Center.NotFound");
            }

            var feedback = new Feedback
            {
                CenterId = request.CenterId,
                SenderName = request.Name.Trim(),
                SenderEmail = request.Email.Trim(),
                SenderPhone = request.Phone?.Trim(),
                Type = request.Type,
                Subject = request.Subject.Trim(),
                Message = request.Message.Trim()
            };

            await SaveAndNotifySuperAdminsAsync(feedback);
            return MapToUserDto(feedback);
        }

        public async Task<PagedResult<FeedbackResponseDto>> GetMyFeedbacksAsync(int centerId, int userId, PagedRequest request)
        {
            var query = sharedDbContext.Feedbacks
                .AsNoTracking()
                .Where(f => f.CenterId == centerId && f.SenderUserId == userId)
                .OrderByDescending(f => f.CreatedAt);

            var page = await query.ToPagedResultAsync(request.PageNumber, request.PageSize);

            return new PagedResult<FeedbackResponseDto>
            {
                Items = page.Items.Select(MapToUserDto).ToList(),
                TotalCount = page.TotalCount,
                PageNumber = page.PageNumber,
                PageSize = page.PageSize
            };
        }



        public async Task<PagedResult<AdminFeedbackResponseDto>> GetFeedbacksAsync(PagedRequest request, FeedbackStatus? status, FeedbackType? type, int? centerId)
        {
            IQueryable<Feedback> query = sharedDbContext.Feedbacks
                .AsNoTracking()
                .Include(f => f.Center);

            if (status.HasValue)
                query = query.Where(f => f.Status == status.Value);

            if (type.HasValue)
                query = query.Where(f => f.Type == type.Value);

            if (centerId.HasValue)
                query = query.Where(f => f.CenterId == centerId.Value);

            query = query.OrderByDescending(f => f.CreatedAt);

            var page = await query.ToPagedResultAsync(request.PageNumber, request.PageSize);

            return new PagedResult<AdminFeedbackResponseDto>
            {
                Items = page.Items.Select(MapToAdminDto).ToList(),
                TotalCount = page.TotalCount,
                PageNumber = page.PageNumber,
                PageSize = page.PageSize
            };
        }

        public async Task<AdminFeedbackResponseDto> UpdateStatusAsync(int feedbackId, FeedbackStatus status)
        {
            var feedback = await sharedDbContext.Feedbacks
                .Include(f => f.Center)
                .FirstOrDefaultAsync(f => f.Id == feedbackId);

            if (feedback == null)
                throw new BusinessException("Feedback.NotFound");

            feedback.Status = status;
            feedback.UpdatedAt = DateTime.UtcNow;
            await sharedDbContext.SaveChangesAsync();

            return MapToAdminDto(feedback);
        }

        public async Task<AdminFeedbackResponseDto> ReplyAsync(int feedbackId, int superAdminId, ReplyFeedbackRequestDto request)
        {
            var feedback = await sharedDbContext.Feedbacks
                .Include(f => f.Center)
                .FirstOrDefaultAsync(f => f.Id == feedbackId);

            if (feedback == null)
                throw new BusinessException("Feedback.NotFound");

            var now = DateTime.UtcNow;
            feedback.Reply = request.Reply.Trim();
            feedback.RepliedAt = now;
            feedback.RepliedBySuperAdminId = superAdminId;
            feedback.Status = request.Status;
            feedback.UpdatedAt = now;
            await sharedDbContext.SaveChangesAsync();


            await DeliverReplyAsync(feedback);

            return MapToAdminDto(feedback);
        }

        public async Task<PagedResult<SuperAdminNotificationResponseDto>> GetSuperAdminNotificationsAsync(int superAdminId, PagedRequest request)
        {
            var query = sharedDbContext.SuperAdminNotifications
                .AsNoTracking()
                .Where(n => n.SuperAdminId == superAdminId)
                .OrderByDescending(n => n.CreatedAt)
                .Select(n => new SuperAdminNotificationResponseDto
                {
                    NotificationId = n.Id,
                    FeedbackId = n.FeedbackId,
                    Message = n.Message,
                    IsRead = n.IsRead,
                    CreatedAt = n.CreatedAt
                });

            return await query.ToPagedResultAsync(request.PageNumber, request.PageSize);
        }

        public async Task<int> GetUnreadSuperAdminNotificationsCountAsync(int superAdminId)
        {
            return await sharedDbContext.SuperAdminNotifications
                .CountAsync(n => n.SuperAdminId == superAdminId && !n.IsRead);
        }

        public async Task MarkSuperAdminNotificationReadAsync(int superAdminId, int notificationId)
        {
            var notification = await sharedDbContext.SuperAdminNotifications
                .FirstOrDefaultAsync(n => n.Id == notificationId && n.SuperAdminId == superAdminId);

            if (notification == null)
                throw new BusinessException("Notification.NotFound");

            notification.IsRead = true;
            await sharedDbContext.SaveChangesAsync();
        }

        public async Task MarkAllSuperAdminNotificationsReadAsync(int superAdminId)
        {
            await sharedDbContext.SuperAdminNotifications
                .Where(n => n.SuperAdminId == superAdminId && !n.IsRead)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));
        }


        private async Task SaveAndNotifySuperAdminsAsync(Feedback feedback)
        {
            sharedDbContext.Feedbacks.Add(feedback);

            var superAdminIds = await sharedDbContext.SuperAdmins
                .Where(s => s.IsActive)
                .Select(s => s.Id)
                .ToListAsync();

            var message = Truncate(
                $"رسالة جديدة ({TypeLabel(feedback.Type)}) من {feedback.SenderName}: {feedback.Subject}", 300);

            foreach (var superAdminId in superAdminIds)
            {
                sharedDbContext.SuperAdminNotifications.Add(new SuperAdminNotification
                {
                    SuperAdminId = superAdminId,
                    Feedback = feedback,
                    Message = message
                });
            }

            await sharedDbContext.SaveChangesAsync();
        }


        private async Task DeliverReplyAsync(Feedback feedback)
        {
            try
            {
                if (feedback.CenterId.HasValue && feedback.SenderUserId.HasValue)
                {
                    using var db = contextFactory.CreateForCenter(feedback.CenterId.Value);

                    db.Notifications.Add(new Notification
                    {
                        UserId = feedback.SenderUserId.Value,
                        Type = NotificationType.Feedback,
                        Message = $"تم الرد على رسالتك \"{feedback.Subject}\". يمكنك قراءة الرد من قائمة رسائلك.",
                        CreatedAt = DateTime.UtcNow,
                        IsRead = false
                    });

                    await db.SaveChangesAsync();
                }
                else
                {
                    await emailService.SendCustomMessageAsync(
                        feedback.SenderEmail,
                        $"رد على: {feedback.Subject}",
                        feedback.Reply!);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to deliver reply of feedback {FeedbackId}", feedback.Id);
            }
        }

        private static FeedbackResponseDto MapToUserDto(Feedback f) => new()
        {
            FeedbackId = f.Id,
            Type = f.Type.ToString(),
            Status = f.Status.ToString(),
            Subject = f.Subject,
            Message = f.Message,
            CreatedAt = f.CreatedAt,
            Reply = f.Reply,
            RepliedAt = f.RepliedAt
        };

        private static AdminFeedbackResponseDto MapToAdminDto(Feedback f) => new()
        {
            FeedbackId = f.Id,
            CenterId = f.CenterId,
            CenterName = f.Center?.Name,
            SenderUserId = f.SenderUserId,
            SenderRole = f.SenderRole,
            SenderName = f.SenderName,
            SenderEmail = f.SenderEmail,
            SenderPhone = f.SenderPhone,
            IsGuest = f.SenderUserId == null,
            Type = f.Type.ToString(),
            Status = f.Status.ToString(),
            Subject = f.Subject,
            Message = f.Message,
            CreatedAt = f.CreatedAt,
            UpdatedAt = f.UpdatedAt,
            Reply = f.Reply,
            RepliedAt = f.RepliedAt
        };

        private static string TypeLabel(FeedbackType type) => type switch
        {
            FeedbackType.Bug => "مشكلة",
            FeedbackType.Help => "طلب مساعدة",
            FeedbackType.Suggestion => "اقتراح",
            _ => "ملاحظة"
        };

        private static string Truncate(string value, int max) =>
            value.Length <= max ? value : value[..max];
    }
}
