

namespace Sehatak.Application.DTOs.StaffLogIn
{
    public class StaffLoginResponseDto
    {
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public string CenterStatus { get; set; } = string.Empty;
    }
}
