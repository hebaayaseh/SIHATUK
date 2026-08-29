using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.DTOs.DashBoardDto
{
    public class AppointmentsSummaryDto
    {
        public DateOnly Date { get; set; }
        public int Total { get; set; }
        public int Confirmed { get; set; }
        public int Completed { get; set; }
        public int Cancelled { get; set; }
        public int NoShow { get; set; }
        public int WaitlistCount { get; set; }
    }
}
