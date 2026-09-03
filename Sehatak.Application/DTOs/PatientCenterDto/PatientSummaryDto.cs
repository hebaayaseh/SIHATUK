using Sehatak.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.DTOs.PatientCenter
{
    public class PatientSummaryDto
    {
        public TimeOnly? timeSlot {  get; set; }
        public DateOnly date {  get; set; }
        public AppointmentStatus status { get; set; }
        public string DoctorName { get; set; }

    }
}
