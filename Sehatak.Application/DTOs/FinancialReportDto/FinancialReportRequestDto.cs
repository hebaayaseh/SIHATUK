using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.DTOs.FinancialReport
{
    public class FinancialReportRequestDto
    {
        public int Year { get; set; }

        public int? Month { get; set; }
    }
}
