

namespace Sehatak.Application.DTOs.EmergencyDto
{
    public class EmergencyResponseDto
    {
        public int Id { get; set; }
        public int ReceptionistId { get; set; }
        public string PatientName { get; set; }
        public int DoctorUserId { get; set; }
        public bool IsInsurance { get; set; }
        public decimal AmountPaid { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
