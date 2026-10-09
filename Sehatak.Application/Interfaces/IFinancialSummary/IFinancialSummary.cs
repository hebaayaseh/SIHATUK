using Sehatak.Application.DTOs.FinancialReportDto;


namespace Sehatak.Application.Interfaces.IFinancialSummary
{
    public interface IFinancialSummary
    {
        Task<MonthlySummaryDto> GetMonthlySummaryAsync(int centerId, int userId, int year, int month, bool includeDays);
    }
}
