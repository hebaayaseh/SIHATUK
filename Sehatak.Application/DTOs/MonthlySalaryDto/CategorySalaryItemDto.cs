using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.DTOs.MonthlySalaryDto
{
    public class CategorySalaryItemDto
    {
        public string Category { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }
}
