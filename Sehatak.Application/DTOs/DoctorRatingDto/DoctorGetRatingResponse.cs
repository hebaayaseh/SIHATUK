using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.DTOs.DoctorRatingDto
{
    public class DoctorGetRatingResponse
    {
        public double AvrageRating { get; set; } = 0;
        public List<PatientSummaryRating> PatientRatings { get; set; }
    }
}
