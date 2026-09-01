using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.DTOs.DoctorRatingDto
{
    public class GetMyRatingsResponse
    {
        public int ratingId { get; set; }
        public int doctorId { get; set; }
        public string doctorName { get; set; }
        public int AppointmentId { get; set; }
        public int Rating { get; set; }
        public string? Review { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdateAt { get; set; }
    }
}
