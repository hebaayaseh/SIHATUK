using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.DTOs.MonthlySalaryDto
{
    public class MonthlySalariesResponseDto
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public List<CategorySalaryItemDto> Categories { get; set; } = new();
        public List<DoctorSalaryItemDto> Doctors { get; set; } = new();
        public decimal CategoriesTotal { get; set; }
        public decimal DoctorsTotal { get; set; }
        public decimal TotalSalaries { get; set; }
    }
}
