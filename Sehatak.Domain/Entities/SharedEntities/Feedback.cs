using Sehatak.Domain.Enums.SharedEnums;

namespace Sehatak.Domain.Entities.SharedEntities
{
    public class Feedback
    {
        public int Id { get; set; }

        public int? CenterId { get; set; }

        public int? SenderUserId { get; set; }
        public string? SenderRole { get; set; }

        public string SenderName { get; set; } = string.Empty;
        public string SenderEmail { get; set; } = string.Empty;
        public string? SenderPhone { get; set; }

        public FeedbackType Type { get; set; }
        public string Subject { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;

        public FeedbackStatus Status { get; set; } = FeedbackStatus.New;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        public string? Reply { get; set; }
        public DateTime? RepliedAt { get; set; }
        public int? RepliedBySuperAdminId { get; set; }

        // Navigation Properties :
        public MedicalCenter? Center { get; set; }
        public SuperAdmin? RepliedBySuperAdmin { get; set; }
    }
}
