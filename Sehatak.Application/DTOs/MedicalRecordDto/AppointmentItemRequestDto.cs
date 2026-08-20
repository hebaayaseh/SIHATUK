namespace Sehatak.Application.DTOs.MedicalRecordDto
{
    public class AppointmentItemRequestDto
    {
        public int ServicePriceId { get; set; }
        public int Quantity { get; set; } = 1;
    }
}