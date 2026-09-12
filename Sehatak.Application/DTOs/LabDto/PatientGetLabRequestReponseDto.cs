

namespace Sehatak.Application.DTOs.LabDto
{
    public class PatientGetLabRequestReponseDto
    {
        public int LabRequestId { get; set; }
        public string? Note { get; set; }
        public decimal TotalPrice { get; set; }
        public string LabStatus { get; set; } = "Pending";
        public List<LabItemResponseDto>? LabItems { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
