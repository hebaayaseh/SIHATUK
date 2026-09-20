using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace Sehatak.Application.DTOs.StaffSignup
{
    public class DoctorRequestDto
    {
        public string doctorFirstName { get; set; }
        public string doctorLastName { get; set; }

        [Required]
        [EmailAddress]
        [MaxLength(150)]
        public string email { get; set; }

        [Phone]
        [MaxLength(20)]
        public string? phoneNumber { get; set; }
        public string address { get; set; }
        public string city { get; set; }
        public IFormFile? ProfileImage { get; set; }
        public string Specialization { get; set; }
        public string Bio { get; set; }
        public bool OnlineEnabled { get; set; } = false;
        public int departmentId {  get; set; }
    }
}
