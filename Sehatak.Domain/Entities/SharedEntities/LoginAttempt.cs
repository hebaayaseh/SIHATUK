using Sehatak.Domain.Enums.SharedEnums;

namespace Sehatak.Domain.Entities.SharedEntities
{
    public class LoginAttempt
    {
        public int Id { get; set; }
        public int CenterId { get; set; }
        public string Email { get; set; } = null!;
        public string? IpAddress { get; set; }
        public bool Success { get; set; }
        public LoginUserType UserType { get; set; }
        public DateTime AttemptedAt { get; set; }
    }
}