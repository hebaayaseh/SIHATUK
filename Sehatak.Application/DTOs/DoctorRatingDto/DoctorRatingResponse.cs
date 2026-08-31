using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.DTOs.DoctorRaitingDto
{
    public class DoctorRatingResponse
    {
        public int Id { get; set; }

        public int DoctorId { get; set; }
        public string PatientName {  get; set; }
        public int PatientId { get; set; }

        public int AppointmentId { get; set; }

        public int Rating { get; set; }

        public string? Review { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdateAt { get; set; }
    }
}
