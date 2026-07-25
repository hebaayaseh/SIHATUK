using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.DTOs.AppointmentDto
{
    public class RescheduleAppointmentRequest
    {
        public int appointmentId {  get; set; }
        public TimeOnly timeSlot { get; set; }
        public DateOnly date {  get; set; }
    }
}
