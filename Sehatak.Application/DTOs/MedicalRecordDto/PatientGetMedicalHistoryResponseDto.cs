

namespace Sehatak.Application.DTOs.MedicalRecordDto
{
    public class PatientGetMedicalHistoryResponseDto
    {
        public int medicalRecordId {  get; set; }
        public int DoctorId { get; set; }
        public string DoctorName { get; set; } = string.Empty;
        public string? Diagnosis { get; set; }
        public string? Prescription { get; set; } = string.Empty;
        public string? Notes { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdateAt { get; set; }
    }
}
