using Microsoft.AspNetCore.Http;

namespace Sehatak.Application.DTOs.CreateCenterRequestDto
{
    public class CenterRegistrationRequestDto
    {
        public string CenterName { get; set; }
        public string CenterAddress { get; set; } = string.Empty;
        public string CenterPhone { get; set; } = string.Empty;
        public string AdminFirstName { get; set; } = string.Empty;
        public string AdminLastName { get; set; } = string.Empty;
        public int PlanId { get; set; }
        public string AdminEmail { get; set; } = string.Empty;
        public string AdminPhone { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public IFormFile? LogoUrl { get; set; }
        public bool RequiresPrepayment { get; set; } = false;

        public decimal PrepaymentAmount { get; set; } = 0;

        public int RefundPolicyHours { get; set; } = 24;

        public decimal PartialRefundPercent { get; set; }
    }
}
