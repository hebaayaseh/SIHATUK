using Sehatak.Application.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.DTOs.GetStaffDto
{
    public class GetDoctorsResponseDto : PagedRequest
    {
        public int DepartmentId { get; set; }
        public string DepartmentName { get; set; }
        public string? DepartmentDescription { get; set; }
        public string? DepartmentImageUrl { get; set; }
        public List<DoctorsSummaryDto> Doctors {  get; set; }
    }
}
