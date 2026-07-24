using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Domain.Entities.TenantEntities
{
    public class DoctorBlockedDay
    {
        public int Id {  get; set; }
        public int doctorId { get; set; }
        public DateOnly date {  get; set; }
        public TimeOnly? timeSlot {  get; set; }
        public bool isBlocked { get; set; }
        public string? Reason { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation Proparity : 

        public Doctor Doctor { get; set; } = null!;
    }
}
