using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.DTOs.MedicalRecordDto
{
    public class UpdateMedicalRecordRequestDto
    {
        public int PatientId { get; set; }
        public int MedicalRecordId {  get; set; }
        public int? AppointmentId { get; set; }
        public int? ConsultationId { get; set; }
        public string? Prescription { get; set; }
        public string? Notes { get; set; }
        public string? Diagnosis { get; set; }
        public List<int>? RemoveItemIds { get; set; }   // Items Id
        public List<AppointmentItemRequestDto>? Items { get; set; }
        public decimal? CustomConsultationPrice { get; set; }
    }
}
