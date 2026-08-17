using Sehatak.Domain.Enums;

namespace Sehatak.Domain.Entities.TenantEntities
{
    public class StaffShift
    {

        public int Id { get; set; }

        public int UserId { get; set; }

        public DateOnly ShiftDate { get; set; }
        public ShiftGroup ShiftName { get; set; } 
            
        public bool IsActive { get; set; } = true;

        //  Navigation Properties :
        public User Staff { get; set; } = null!;
    }
}
