using Sehatak.Domain.Entities.TenantEntities;

namespace Sehatak.Domain.Entities.General
{
    public class EmailVerificationCode
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string Code { get; set; } = string.Empty;     
        public DateTime ExpiresAt { get; set; } 
        public bool IsUsed { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string Purpose { get; set; } = string.Empty;
        public int Attempts { get; set; }
        public string? PendingValue { get; set; }
        // Navigation property 
        public User User { get; set; } = null!;
    }
}
