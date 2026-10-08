using Sehatak.Domain.Enums;

namespace Sehatak.Application.DTOs.MonthlySalaryDto
{
    public class SetCategorySalaryRequestDto
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public SalaryCategory Category { get; set; }
        public decimal Amount { get; set; }
    }
}
