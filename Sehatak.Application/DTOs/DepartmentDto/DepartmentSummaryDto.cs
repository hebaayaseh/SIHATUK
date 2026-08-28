using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.DTOs.DepartmentDto
{
    public class DepartmentSummaryDto
    {
        public int Id { get; set; }
        public string departmentName { get; set; }
        public string? departmentDescription { get; set; }
        public string? logo { get; set; }
    }
}
