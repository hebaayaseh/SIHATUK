using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.DTOs.MedicalRecordDto
{
    public class MedicalRecordDetailRequestDto
    {
        public int PatientId { get; set; }
        public int? AppointmentId { get; set; }
        public int? ConsultationId { get; set; }
        public string? Diagnosis { get; set; }
        public string Prescription { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public decimal? ConsultationCost { get; set; }
        public List<MedicalRecordItemDto>? Items { get; set; }
    }
}
