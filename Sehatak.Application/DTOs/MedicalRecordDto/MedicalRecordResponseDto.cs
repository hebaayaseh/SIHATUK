using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.DTOs.MedicalRecordDto
{
    public class MedicalRecordResponseDto
    {
        public string Prescription { get; set; }
        public string Notes { get; set; }
        public string? Diagnosis { get; set; }
    }
}
