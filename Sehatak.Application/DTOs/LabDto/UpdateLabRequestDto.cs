

namespace Sehatak.Application.DTOs.LabDto
{
    public class UpdateLabRequestDto
    {
        public int LabRequestId { get; set; }
        public int PatientId { get; set; }
        public int AppointmentId { get; set; }
        public string? Note { get; set; }
        public List<LabRequestItemsSummaryDto>? AddLabItems { get; set; }
        public List<int>? RemoveLabItems { get; set; }
    }
}
