using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.DTOs.AppointmentDto
{
    public class CancelAppointmentRequest
    {
        public TimeOnly timeSlot {  get; set; }
        public DateOnly date {  get; set; }
        public string Resone {  get; set; }

    }
}
