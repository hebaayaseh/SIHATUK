using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.DTOs.FinancialReport;
using Sehatak.Application.Interfaces.IFinancialReports;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;

namespace Sehatak.Infrastructure.Services.FinancialReportServices
{
    public class FinancialReportService : IFinancialReport
    {
        private readonly SharedDbContext sharedDbContext;
        public FinancialReportService(SharedDbContext sharedDbContext)
        {
            this.sharedDbContext = sharedDbContext;
        }

        public async Task<byte[]> GenerateReportAsync(FinancialReportRequestDto request)
        {
            if (request.Year < 0)
                throw new BusinessException("Validation.InvalidYear");

            DateTime periodStart;
            DateTime periodEnd;

            if(request.Month.HasValue)
            {
                if(request.Month.Value<0||request.Month.Value>12)
                    throw new BusinessException("Validation.InvalidMonth");

                periodStart = new DateTime(request.Year, request.Month.Value, 1);
                periodEnd = periodStart.AddMonths(1).AddDays(-1);

            }
            else
            {
                periodStart = new DateTime(request.Year, 1, 1);
                periodEnd = new DateTime(request.Year, 12, 31);

            }

            var confirmedPayments = await sharedDbContext.subscriptionPayments
                .Include(c => c.Center)
                .Where(c => c.RecordedBySuperAdminId != null
                 && c.PaidAt >= periodStart
                 && c.PaidAt <= periodEnd)
                .Select(p => new
                {
                    CenterName = p.Center.Name,
                    p.Amount
                })
                .ToListAsync();

            var activeSubscriptionsAmounts = await sharedDbContext.CenterSubscriptions
                .Include(c => c.Center)
                .Where(c => c.Status == SubscriptionStatus.Active
                 && c.StartDate >= DateOnly.FromDateTime(periodStart)
                 && c.StartDate <= DateOnly.FromDateTime(periodEnd))
                 .Select(p => new
                 {

                     CenterName = p.Center.Name,
                     p.AmountPaid

                 }).ToListAsync();

            using var workbook = new XLWorkbook();
            var paymentSheet = workbook.Worksheets.Add("Payment confirmed");
            paymentSheet.Cell(1,1).Value= "Center";
            paymentSheet.Cell(1,2).Value= "Amount";
            paymentSheet.Range(1, 1, 1, 2).Style.Font.Bold = true;
            paymentSheet.Range(1, 1, 1, 2).SortLeftToRight();

            var paymentByCenter = confirmedPayments
                .GroupBy(c=>c.CenterName)
                .Select(c=>new {CenterName = c.Key, Total = c.Sum(x=>x.Amount)})
                .OrderByDescending(x => x.Total)
                .ToList();

            int row = 2;
            foreach(var item in paymentByCenter)
            {
                paymentSheet.Cell(row, 1).Value = item.CenterName;
                paymentSheet.Cell(row, 2).Value = item.Total;
                row++;
            }

            paymentSheet.Cell(row, 1).Value = "Amount Total";
            paymentSheet.Cell(row, 1).Style.Font.Bold = true;
            paymentSheet.Cell(row,2).Value= paymentByCenter.Sum(x => x.Total);
            paymentSheet.Cell(row, 1).Style.Font.Bold = true;
            paymentSheet.Columns().AdjustToContents();

            var subSheet = workbook.Worksheets.Add("Active Supscription");
            subSheet.Cell(row, 1).Value = "Center";
            subSheet.Cell(row, 2).Value = "Amount";
            subSheet.Range(1, 1, 1, 2).Style.Font.Bold = true;
            paymentSheet.Range(1, 1, 1, 2).SortLeftToRight();

            var subByCenter = activeSubscriptionsAmounts
                .GroupBy(c => c.CenterName)
                .Select(c => new { CenterName = c.Key, Total = c.Sum(x => x.AmountPaid) })
                .OrderByDescending(x => x.Total)
                .ToList();

            row = 2;
            foreach(var item in subByCenter)
            {
                subSheet.Cell(row, 1).Value = item.CenterName;
                subSheet.Cell(row, 2).Value = item.Total;
                row++;

            }

            subSheet.Cell(row, 1).Value = "Total Amount";
            subSheet.Cell(row, 1).Style.Font.Bold = true;
            subSheet.Cell(row, 2).Value = subByCenter.Sum(x => x.Total);
            subSheet.Cell(row, 2).Style.Font.Bold = true;
            subSheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();

        }
    }
}
