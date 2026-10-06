using Sehatak.Domain.Enums;

namespace Sehatak.Application.DTOs.ServicePriceDto
{
    public class UpaterServicePriceResponse
    {
        public int Id {  get; set; }
        public string ServiceName { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public ServiceType Type { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
