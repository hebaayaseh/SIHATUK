using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
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
            if (request.Year < 1 || request.Year > 9998)
                throw new BusinessException("Validation.InvalidYear");

            if (request.Month.HasValue && (request.Month.Value < 1 || request.Month.Value > 12))
                throw new BusinessException("Validation.InvalidMonth");


            var periodStart = request.Month.HasValue
                ? new DateTime(request.Year, request.Month.Value, 1)
                : new DateTime(request.Year, 1, 1);
            var periodEnd = request.Month.HasValue
                ? periodStart.AddMonths(1)
                : periodStart.AddYears(1);


            var fromUtc = ClinicClock.ToUtc(periodStart);
            var toUtc = ClinicClock.ToUtc(periodEnd);
            var fromDate = DateOnly.FromDateTime(periodStart);
            var toDate = DateOnly.FromDateTime(periodEnd);

            var confirmedPayments = await sharedDbContext.subscriptionPayments
                .Where(p => p.RecordedBySuperAdminId != null
                         && p.PaidAt >= fromUtc
                         && p.PaidAt < toUtc)
                .Select(p => new { CenterName = p.Center!.Name, p.Amount })
                .ToListAsync();

            var activeSubscriptionsAmounts = await sharedDbContext.CenterSubscriptions
                .Where(c => c.Status == SubscriptionStatus.Active
                         && c.StartDate >= fromDate
                         && c.StartDate < toDate)
                .Select(c => new { CenterName = c.Center.Name, c.AmountPaid })
                .ToListAsync();

            using var workbook = new XLWorkbook();


            var paymentSheet = workbook.Worksheets.Add("Payment confirmed");
            paymentSheet.Cell(1, 1).Value = "Center";
            paymentSheet.Cell(1, 2).Value = "Amount";
            paymentSheet.Range(1, 1, 1, 2).Style.Font.Bold = true;

            var paymentByCenter = confirmedPayments
                .GroupBy(c => c.CenterName)
                .Select(c => new { CenterName = c.Key, Total = c.Sum(x => x.Amount) })
                .OrderByDescending(x => x.Total)
                .ToList();

            var row = 2;
            foreach (var item in paymentByCenter)
            {
                paymentSheet.Cell(row, 1).Value = item.CenterName;
                paymentSheet.Cell(row, 2).Value = item.Total;
                row++;
            }

            paymentSheet.Cell(row, 1).Value = "Amount Total";
            paymentSheet.Cell(row, 2).Value = paymentByCenter.Sum(x => x.Total);
            paymentSheet.Range(row, 1, row, 2).Style.Font.Bold = true;
            paymentSheet.Columns().AdjustToContents();


            var subSheet = workbook.Worksheets.Add("Active Subscription");
            subSheet.Cell(1, 1).Value = "Center";
            subSheet.Cell(1, 2).Value = "Amount";
            subSheet.Range(1, 1, 1, 2).Style.Font.Bold = true;

            var subByCenter = activeSubscriptionsAmounts
                .GroupBy(c => c.CenterName)
                .Select(c => new { CenterName = c.Key, Total = c.Sum(x => x.AmountPaid) })
                .OrderByDescending(x => x.Total)
                .ToList();

            row = 2;
            foreach (var item in subByCenter)
            {
                subSheet.Cell(row, 1).Value = item.CenterName;
                subSheet.Cell(row, 2).Value = item.Total;
                row++;
            }

            subSheet.Cell(row, 1).Value = "Total Amount";
            subSheet.Cell(row, 2).Value = subByCenter.Sum(x => x.Total);
            subSheet.Range(row, 1, row, 2).Style.Font.Bold = true;
            subSheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }
    }
}
