using Sehatak.Domain.Enums.SharedEnums;

namespace Sehatak.Application.DTOs.FeedbackDto
{

    public class SubmitFeedbackRequestDto
    {
        public FeedbackType Type { get; set; }
        public string Subject { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    // visitor without an account
    public class SubmitGuestFeedbackRequestDto
    {
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public int? CenterId { get; set; }
        public FeedbackType Type { get; set; }
        public string Subject { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    public class ReplyFeedbackRequestDto
    {
        public string Reply { get; set; } = string.Empty;
        public FeedbackStatus Status { get; set; } = FeedbackStatus.Resolved;
    }

    public class UpdateFeedbackStatusRequestDto
    {
        public FeedbackStatus Status { get; set; }
    }


    public class FeedbackResponseDto
    {
        public int FeedbackId { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string? Reply { get; set; }
        public DateTime? RepliedAt { get; set; }
    }


    public class AdminFeedbackResponseDto
    {
        public int FeedbackId { get; set; }
        public int? CenterId { get; set; }
        public string? CenterName { get; set; }
        public int? SenderUserId { get; set; }
        public string? SenderRole { get; set; }
        public string SenderName { get; set; } = string.Empty;
        public string SenderEmail { get; set; } = string.Empty;
        public string? SenderPhone { get; set; }
        public bool IsGuest { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string? Reply { get; set; }
        public DateTime? RepliedAt { get; set; }
    }

    public class SuperAdminNotificationResponseDto
    {
        public int NotificationId { get; set; }
        public int? FeedbackId { get; set; }
        public string Message { get; set; } = string.Empty;
        public bool IsRead { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
