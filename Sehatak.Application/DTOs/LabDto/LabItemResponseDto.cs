

namespace Sehatak.Application.DTOs.LabDto
{
    public class LabItemResponseDto
    {
        public int ServicePriceId { get; set; }
        public string ServiceName { get; set; }
        public int ItemId { get; set; }
        public decimal UnitPrice { get; set; }
        public bool IsAvailable { get; set; }
    }
}
