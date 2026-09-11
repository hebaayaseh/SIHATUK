using Sehatak.Domain.Enums;

namespace Sehatak.Application.DTOs.ViewProfileDto
{
    public class ViewPatientProfileResponseDto
    {
        public int Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public string? PhoneNumber { get; set; }
        public string City { get; set; }
        public string Adreess { get; set; }
        public BloodType BloodType { get; set; }
        public Gender Gender { get; set; }
        public string? ProfileImage { get; set; }
        public DateOnly DateOfBith { get; set; }
    }
}
