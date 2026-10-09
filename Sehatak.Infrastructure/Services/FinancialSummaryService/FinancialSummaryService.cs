using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.DTOs.FinancialReportDto;
using Sehatak.Application.Interfaces.IFinancialSummary;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.PaymentEnums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;
using Sehatak.Infrastructure.Services.SalaryServices;

namespace Sehatak.Infrastructure.Services.FinancialSummaryService
{
    public class FinancialSummaryService : IFinancialSummary
    {
        private readonly SharedDbContext sharedDbContext;
        private readonly TenantDbContextFactory contextFactory;
        private readonly SalaryService salaryService;

        public FinancialSummaryService(SharedDbContext sharedDbContext, TenantDbContextFactory contextFactory, SalaryService salaryService)
        {
            this.sharedDbContext = sharedDbContext;
            this.contextFactory = contextFactory;
            this.salaryService = salaryService;
        }

        public async Task<MonthlySummaryDto> GetMonthlySummaryAsync(int centerId, int userId, int year, int month, bool includeDays)
        {
            if (year < 1 || year > 9999)
                throw new BusinessException("Validation.InvalidYear");

            if (month < 1 || month > 12)
                throw new BusinessException("Validation.InvalidMonth");

            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var admin = await db.Users
                .FirstOrDefaultAsync(a => a.Id == userId
                                     && a.role == userRole.Admin
                                     && a.isActive);

            if (admin == null)
                throw new BusinessException("Auth.Forbidden");

            var start = new DateOnly(year, month, 1);
            var end = start.AddMonths(1);                                   
            var utcFrom = start.ToDateTime(TimeOnly.MinValue).AddDays(-1);  
            var utcTo = end.ToDateTime(TimeOnly.MinValue).AddDays(1);

            var offset = TimeSpan.FromMinutes(Math.Round((ClinicClock.Now - DateTime.UtcNow).TotalMinutes));
            DateOnly ToClinicDate(DateTime utc) => DateOnly.FromDateTime(utc.Add(offset));

            var payments = await db.Payments
                .Where(p => p.Status == PaymentStatus.Paid
                       && p.PaidAt >= utcFrom && p.PaidAt < utcTo
                       && (p.Type == PaymentType.Appointment
                           || p.Type == PaymentType.Consultation
                           || p.Type == PaymentType.Prepayment
                           || p.Type == PaymentType.Lab))
                .Select(p => new { p.Type, p.Method, p.Amount, p.PaidAt })
                .ToListAsync();

            var emergencies = await db.EmergencyCases
                .Where(e => e.CreatedAt >= utcFrom && e.CreatedAt < utcTo)
                .Select(e => new { e.AmountPaid, e.IsInsurance, e.CreatedAt })
                .ToListAsync();

            var expenses = await db.Expenses
                .Where(e => e.ExpenseDate >= start && e.ExpenseDate < end)
                .Select(e => new { e.Section, e.Amount, e.ExpenseDate })
                .ToListAsync();

            var days = Enumerable.Range(0, end.DayNumber - start.DayNumber)
                .Select(i => new DailySummaryDto { Date = start.AddDays(i) })
                .ToDictionary(d => d.Date);

            foreach (var p in payments)
            {
                if (!days.TryGetValue(ToClinicDate(p.PaidAt), out var day))
                    continue;

                var section = p.Type == PaymentType.Lab ? day.Lab : day.Specialty;
                section.Receipts += p.Amount;
                if (p.Method == PaymentMethod.card)
                    section.CardReceipts += p.Amount;
            }

            foreach (var e in emergencies)
            {
                if (!days.TryGetValue(ToClinicDate(e.CreatedAt), out var day))
                    continue;

                day.GeneralMedicine.Receipts += e.AmountPaid;
                if (e.IsInsurance)
                    day.GeneralInsuranceReceipts += e.AmountPaid;
            }

            foreach (var x in expenses)
            {
                var day = days[x.ExpenseDate];
                switch (x.Section)
                {
                    case ExpenseSection.GeneralMedicine: day.GeneralMedicine.Expenses += x.Amount; break;
                    case ExpenseSection.Lab: day.Lab.Expenses += x.Amount; break;
                    case ExpenseSection.Specialty: day.Specialty.Expenses += x.Amount; break;
                    case ExpenseSection.Center: day.CenterExpenses += x.Amount; break;
                }
            }

            var list = days.Values.OrderBy(d => d.Date).ToList();

            SectionAmountsDto SumSection(Func<DailySummaryDto, SectionAmountsDto> pick) => new SectionAmountsDto
            {
                Receipts = list.Sum(d => pick(d).Receipts),
                Expenses = list.Sum(d => pick(d).Expenses),
                CardReceipts = list.Sum(d => pick(d).CardReceipts)
            };

            var salaries = await salaryService.LoadMonthlySalariesAsync(db, year, month);

            var summary = new MonthlySummaryDto
            {
                Year = year,
                Month = month,
                Days = includeDays ? list : new List<DailySummaryDto>(),
                GeneralMedicine = SumSection(d => d.GeneralMedicine),
                Lab = SumSection(d => d.Lab),
                Specialty = SumSection(d => d.Specialty),
                GeneralInsuranceReceipts = list.Sum(d => d.GeneralInsuranceReceipts),
                CenterExpenses = list.Sum(d => d.CenterExpenses),
                MonthTotal = list.Sum(d => d.DayTotal),
                Salaries = salaries
            };

            summary.NetProfit = summary.MonthTotal - summary.CenterExpenses - salaries.TotalSalaries;

            return summary;
        }
    }
}
