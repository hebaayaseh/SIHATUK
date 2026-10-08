using Sehatak.Application.DTOs.MonthlySalaryDto;


namespace Sehatak.Application.Interfaces.ISalary
{
    public interface ISalary
    {
        Task<CategorySalaryItemDto> SetCategorySalaryAsync(int centerId, int userId, SetCategorySalaryRequestDto request);
        Task<DoctorSalaryItemDto> SetDoctorSalaryAsync(int centerId, int userId, SetDoctorSalaryRequestDto request);
        Task<MonthlySalariesResponseDto> GetSalariesAsync(int centerId, int userId, int year, int month);
    }
}
