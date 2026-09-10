
using Sehatak.Domain.Enums;

namespace Sehatak.Application.DTOs.ServicePriceDto
{
    public class GetServicePriceResponseDto
    {
        public ServiceType Type { get; set; }
        public List<ServicePriceResponseItem> Items { get; set; } = new();
    }
}
