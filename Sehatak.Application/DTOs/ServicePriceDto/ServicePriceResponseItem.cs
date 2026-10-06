

namespace Sehatak.Application.DTOs.ServicePriceDto
{
    public class ServicePriceResponseItem
    {
        public int Id { get; set; }
        public string ServiceName { get; set; } = string.Empty;
        public decimal Price { get; set; }


    }
}
