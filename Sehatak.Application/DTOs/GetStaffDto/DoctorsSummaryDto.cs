using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.DTOs.GetStaffDto
{
    public class DoctorsSummaryDto
    {
        public int DoctorId { get; set; }
        public string DoctorName { get; set; }
        public bool isActive {  get; set; }
        public double AvrageRating { get; set; } = 0.0;
        public List<string?> Reviews { get; set; }
    }
}
