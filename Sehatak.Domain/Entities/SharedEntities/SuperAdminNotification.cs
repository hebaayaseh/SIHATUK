namespace Sehatak.Domain.Entities.SharedEntities
{

    public class SuperAdminNotification
    {
        public int Id { get; set; }
        public int SuperAdminId { get; set; }
        public int? FeedbackId { get; set; }
        public string Message { get; set; } = string.Empty;
        public bool IsRead { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation Properties :
        public SuperAdmin SuperAdmin { get; set; } = null!;
        public Feedback? Feedback { get; set; }
    }
}
