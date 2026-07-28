using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.PaymentEnums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.DTOs.ConsultationDto
{
    public class ConsultationResponse
    {
        public int Id { get; set; }
        public ConsultationStatus Status {  get; set; }
        public PaymentStatus? PaymentStatus { get; set; }
    }
}
