using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.DTOs.AppointmentDto
{
    public class GetDoctorSummaryResponse
    {
        public int DoctorId { get; set; }
        public string DoctorName { get; set; }
        public string Bio { get; set; }
        public string? Specialization { get; set; }
        public double AvrageRating { get; set; } = 0;
        public List<string?> Reviews { get; set; }
    }
}
