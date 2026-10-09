

namespace Sehatak.Application.DTOs.FinancialReportDto
{
    public class SectionAmountsDto
    {
        public decimal Receipts { get; set; }
        public decimal Expenses { get; set; }
        public decimal Total => Receipts - Expenses;
        public decimal CardReceipts { get; set; }     
    }
}
