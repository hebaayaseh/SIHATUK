

namespace Sehatak.Application.DTOs.EditProfileDto.EditEmailOrPasswored
{
    public class ForgetPasswordRequest
    {
        public string Email {  get; set; }
        public string newPassword { get; set; }
        public string confirmPassword { get; set; }
    }
}
