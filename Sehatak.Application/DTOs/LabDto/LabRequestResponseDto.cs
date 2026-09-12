

namespace Sehatak.Application.DTOs.LabDto
{
    public class LabRequestResponseDto
    {
        public int PatientId { get; set; }
        public int AppointmentId { get; set; }
        public string PatientName { get; set; }
        public string? Note { get; set; }
        public decimal TotalPrice { get; set; }
        public List<LabItemResponseDto>? LabItems { get; set; }
        public DateTime CreatedAt {  get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
