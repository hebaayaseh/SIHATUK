

using Sehatak.Domain.Enums;

namespace Sehatak.Domain.Entities.TenantEntities
{
    public class Expense
    {
        public int Id { get; set; }
        public string? Name { get; set; }              
        public decimal Amount { get; set; }
        public ExpenseSection Section { get; set; }
        public DateOnly ExpenseDate { get; set; }     
        public int CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public User CreatedByUser { get; set; } = null!;
    }
}
