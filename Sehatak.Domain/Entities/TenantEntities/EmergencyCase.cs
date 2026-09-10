using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Domain.Entities.TenantEntities
{
    public class EmergencyCase
    {
        public int  Id { get; set; }
        public string  PatientName { get; set; }        
        public int DoctorUserId { get; set; }
        public int ReceptionistId { get; set; }
        public bool IsInsurance {  get; set; }
        public decimal AmountPaid {  get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        // Navication Poparities :
        public User Receptionist { get; set; } = null!;
        public User DoctorUser { get; set; } = null!;

    }
}
