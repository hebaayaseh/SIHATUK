using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace Sehatak.Application.DTOs.SuperAdminDto
{
    public class RegisterSuperAdminRequestDto
    {
        [Required]
        [MaxLength(100)]
        public string name {  get; set; } = string.Empty;
        [Required]
        [EmailAddress]   
        [MaxLength(150)]
        public string email { get; set; } = string.Empty;
        [Required]
        [Phone]          
        [MaxLength(20)]
        public string phoneNumber { get; set; } = string.Empty;
        [Required]
        public string SuperAdminKey { get; set; } = string.Empty;
        [Required]
        [MinLength(8)]
        public string password { get; set; }
        public IFormFile? ProfileImageUrl { get; set; }

    }
}
