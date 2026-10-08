

namespace Sehatak.Application.DTOs.MonthlySalaryDto
{
    public class SetDoctorSalaryRequestDto
    {
        public int DoctorId { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }
        public decimal Amount { get; set; }
    }
}
