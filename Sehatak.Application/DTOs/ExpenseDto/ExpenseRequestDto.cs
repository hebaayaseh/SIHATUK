using Sehatak.Domain.Enums;


namespace Sehatak.Application.DTOs.ExpenseDto
{
    public class ExpenseRequestDto
    {
        public string? Name { get; set; }
        public decimal Amount { get; set; }
        public ExpenseSection Section { get; set; }
        public DateOnly ExpenseDate { get; set; }
    }
}
