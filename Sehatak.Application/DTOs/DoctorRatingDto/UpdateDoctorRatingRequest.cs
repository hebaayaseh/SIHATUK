using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.DTOs.DoctorRatingDto
{
    public class UpdateDoctorRatingRequest
    {
        public int RatingId { get; set; }

        [Range(1, 5)]
        public int? Rating { get; set; }

        [MaxLength(1000)]
        public string? Review { get; set; }
    }
}
