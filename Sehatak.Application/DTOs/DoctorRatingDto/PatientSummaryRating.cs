using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.DTOs.DoctorRatingDto
{
    public class PatientSummaryRating
    {
        public int ratingId { get; set; }
        public int patientId { get; set; }
        public string patientName { get; set; }
        public int AppointmentId { get; set; }
        public int Rating { get; set; }
        public string? Review { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdateAt { get; set; }
    }
}
