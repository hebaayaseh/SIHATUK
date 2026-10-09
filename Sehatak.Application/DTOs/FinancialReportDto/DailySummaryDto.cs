

namespace Sehatak.Application.DTOs.FinancialReportDto
{
    public class DailySummaryDto
    {
        public DateOnly Date { get; set; }
        public SectionAmountsDto GeneralMedicine { get; set; } = new();
        public SectionAmountsDto Lab { get; set; } = new();
        public SectionAmountsDto Specialty { get; set; } = new();
        public decimal GeneralInsuranceReceipts { get; set; }   
        public decimal CenterExpenses { get; set; }
        public decimal DayTotal => GeneralMedicine.Total + Lab.Total + Specialty.Total;
    }
}
