

namespace Sehatak.Application.DTOs.EmergencyDto
{
    public class UpdateEmergencyRequestDto
    {
        public int EmergencyId { get; set; }
        public string? PatientName { get; set; }
        public bool? IsInsurance { get; set; }
        public decimal? AmountPaid { get; set; }
    }
}
