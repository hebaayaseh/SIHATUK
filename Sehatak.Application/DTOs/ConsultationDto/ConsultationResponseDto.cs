using Sehatak.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.DTOs.ConsultationDto
{
    public class ConsultationResponseDto
    {
        public int ConsultationId { get; set; }
        public string patientName { get; set; }
        public DateTime? SchedualeDate { get; set; }


    }
}
