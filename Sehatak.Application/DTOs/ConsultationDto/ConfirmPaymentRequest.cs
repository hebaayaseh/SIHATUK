using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.DTOs.ConsultationDto
{
    public class ConfirmPaymentRequest
    {
        public DateTime ScheduledAt { get; set; }
        public string VideoLink { get; set; }
    }
}
