

using Sehatak.Domain.Enums.SharedEnums;

namespace Sehatak.Application.DTOs.AuditLogDto
{
    public class LoginAttemptResponseDto
    {
        public int Id { get; set; }
        public int CenterId { get; set; }
        public string Email { get; set; } = null!;
        public string? IpAddress { get; set; }
        public LoginUserType UserType { get; set; }
        public DateTime AttemptedAt { get; set; }
    }
}
