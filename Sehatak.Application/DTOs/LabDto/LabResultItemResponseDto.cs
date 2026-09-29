

using Microsoft.AspNetCore.Http;

namespace Sehatak.Application.DTOs.LabDto
{
    public class LabResultItemResponseDto
    {
        public int LabRequestItemId { get; set; }
        public decimal? ResultValue { get; set; }
        public string? ResultFileUrl { get; set; }
        public string ServicePriceName { get; set; }
        public int ServicePriceId { get; set; }
        public bool IsAvailable {get;set;}

    }
}
