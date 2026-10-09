using Sehatak.Application.DTOs.MonthlySalaryDto;


namespace Sehatak.Application.DTOs.FinancialReportDto
{
    public class MonthlySummaryDto
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public List<DailySummaryDto> Days { get; set; } = new();
        public SectionAmountsDto GeneralMedicine { get; set; } = new();
        public SectionAmountsDto Lab { get; set; } = new();
        public SectionAmountsDto Specialty { get; set; } = new();
        public decimal GeneralInsuranceReceipts { get; set; }
        public decimal CenterExpenses { get; set; }
        public decimal MonthTotal { get; set; }                 
        public MonthlySalariesResponseDto Salaries { get; set; } = new();
        public decimal NetProfit { get; set; }                  
    }
}
