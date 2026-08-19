using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.DTOs.MedicalRecordDto
{
    public class MedicalReqordRequestDto
    {
        public int PatientId { get; set; }

        public int DoctorId { get; set; }

        public int? AppointmentId { get; set; }
        public int? ConsultationId { get; set; }
        public string Prescription { get; set; }
        public string Notes { get; set; }
        public string? Diagnosis { get; set; }
    }
}
