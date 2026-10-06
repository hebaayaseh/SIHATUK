using Sehatak.Domain.Enums;
namespace Sehatak.Application.DTOs.ServicePriceDto
{
    public class ServicePriceResponse
    {
        public ServiceType Type { get; set; }
        public List<ServicePriceResponseItem> CreatedItems { get; set; } = new();
        public DateTime CreatedAt { get; set; }
    }
}
