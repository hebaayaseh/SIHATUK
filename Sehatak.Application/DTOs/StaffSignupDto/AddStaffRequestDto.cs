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
    public class AddStaffRequestDto
    {
        public userRole userRole {  get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }

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
    }
}
