using Sehatak.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.DTOs.ExpenseDto
{
    public class UpdateExpenseRequestDto
    {
        public string? Name { get; set; }
        public decimal? Amount { get; set; }
        public ExpenseSection? Section { get; set; }
        public DateOnly? ExpenseDate { get; set; }
    }
}
