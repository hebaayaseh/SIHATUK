using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.DTOs.DoctorDto
{
    public class DoctorAppointmentResponse
    {
        public List<AppointmentSummaryDto> appointments {  get; set; }
    }
}
