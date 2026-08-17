using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.DTOs.Email
{
    public class SendEmailDto
    {
        // "specific", "active", "expired"
        public string Target { get; set; } = string.Empty;

        public int? CenterId { get; set; }

        public string Subject { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
