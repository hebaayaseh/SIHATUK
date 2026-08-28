using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.DTOs.DepartmentDto
{
    public class GetDepartmentResponseDto
    {
        public List<DepartmentSummaryDto> Departments { get; set; }
    }
}
