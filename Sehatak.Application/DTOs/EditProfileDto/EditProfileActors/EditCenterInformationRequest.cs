using Microsoft.AspNetCore.Http;

namespace Sehatak.Application.DTOs.EditProfile.EditProfileActors
{
    public class EditCenterInformationRequest
    {
        public bool? RequiresPrepayment { get; set; } 

        public decimal? PrepaymentAmount { get; set; } 

        public int? RefundPolicyHours { get; set; }

        public decimal? PartialRefundPercent { get; set; }

        public string? AdminWhatsappNumber { get; set; }

        public string? Phone { get; set; }

        public string? Address { get; set; }

        public IFormFile? LogoUrl { get; set; }
    }
}
