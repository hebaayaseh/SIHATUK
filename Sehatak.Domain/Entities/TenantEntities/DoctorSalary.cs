

namespace Sehatak.Domain.Entities.TenantEntities
{
    public class DoctorSalary
    {
        public int Id { get; set; }
        public int DoctorId { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }
        public decimal Amount { get; set; }
        public int UpdatedByUserId { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public Doctor Doctor { get; set; } = null!;
        public User UpdatedByUser { get; set; } = null!;
    }
}
