using Sehatak.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.DTOs.PatientCenter
{
    public class GetPatientRequestDto
    {
        public int userId { get; set; }
        public AppointmentStatus status { get; set; }
    }
}
