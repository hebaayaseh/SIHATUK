using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.DTOs.EditProfile.EditProfileActors
{
    public class EditCenterInformationRequest
    {
        public bool? RequiresPrepayment { get; set; } = false;

        public decimal? PrepaymentAmount { get; set; } = 0;

        public int? RefundPolicyHours { get; set; } = 24;

        public decimal? PartialRefundPercent { get; set; }

        public string? AdminWhatsappNumber { get; set; }

        public string? Phone { get; set; }

        public string? Address { get; set; }

        public IFormFile? LogoUrl { get; set; }
    }
}
