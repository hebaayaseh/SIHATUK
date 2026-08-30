namespace Sehatak.Application.DTOs.MedicalRecordDto
{
    public class MedicalRecordItemDto
    {
        public int Id { get; set; }
        public string ServiceName { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal TotalPrice { get; set; }
    }
}