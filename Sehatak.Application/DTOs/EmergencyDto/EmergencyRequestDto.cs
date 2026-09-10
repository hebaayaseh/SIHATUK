using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.DTOs.EmergencyDto
{
    public class EmergencyRequestDto
    {
        public string PatientName { get; set; }
        public int DoctorUserId { get; set; }
        public bool IsInsurance { get; set; }
        public decimal AmountPaid { get; set; }
    }
}
