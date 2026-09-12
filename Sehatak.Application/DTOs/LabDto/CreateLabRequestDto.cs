

namespace Sehatak.Application.DTOs.LabDto
{
    public class CreateLabRequestDto
    {
        public int PatientId { get; set; }
        public int AppointmentId { get; set; }
        public string? Note { get; set; }
        public List<LabRequestItemsSummaryDto>? LabItems { get; set; }
    }
}
