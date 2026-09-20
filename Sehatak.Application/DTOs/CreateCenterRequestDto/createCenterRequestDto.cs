using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace Sehatak.Application.DTOs.CreateCenterRequestDto
{
    public class createCenterRequestDto
    {
        [Required, MaxLength(256)]
        public string Name { get; set; } = string.Empty;

        [Required, Phone, MaxLength(20)]
        public string Phone { get; set; }

        [Required, MaxLength(500)]
        public string Address { get; set; }

        [Required]
        public int PlanId { get; set; }
        public IFormFile? Logo { get; set; }
        public bool RequiresPrepayment { get; set; } = false;
        public decimal PrepaymentAmount { get; set; } = 0;
        public int RefundPolicyHours { get; set; } = 24;
        public decimal PartialRefundPercent { get; set; }
        public int? AddedBySuperAdminId { get; set; }
        public string? AdminWhatsappNumber { get; set; }
        public string? AdminEmail { get; set; }
    }
}
