
namespace Sehatak.Application.DTOs.AuditLogDto
{
    public class AuditLogResponseDto
    {
        public int Id { get; set; }
        public string Action { get; set; }
        public string EntityName { get; set; }
        public int? EntityId { get; set; }
        public int UserId { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }

    }
}
