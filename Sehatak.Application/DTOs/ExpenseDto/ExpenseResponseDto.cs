

namespace Sehatak.Application.DTOs.ExpenseDto
{
    public class ExpenseResponseDto
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public decimal Amount { get; set; }
        public string Section { get; set; } = string.Empty;
        public DateOnly ExpenseDate { get; set; }
        public int CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
