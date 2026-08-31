using Microsoft.AspNetCore.Http;
using Sehatak.Domain.Enums;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        public DateTime createdAt { get; set; } = DateTime.UtcNow;
        public IFormFile? ProfileImage { get; set; }
        public string Specialization { get; set; }
        public string Bio { get; set; }
        public bool OnlineEnabled { get; set; } = false;
        public int departmentId {  get; set; }
    }
}
