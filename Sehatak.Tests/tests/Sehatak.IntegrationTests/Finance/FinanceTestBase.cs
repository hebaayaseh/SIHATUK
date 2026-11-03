using Microsoft.EntityFrameworkCore;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.IntegrationTests.Payments;
using Sehatak.IntegrationTests.Infrastructure;
using ExpenseSvc = Sehatak.Infrastructure.Services.ExpenseService.ExpenseService;
using SalarySvc = Sehatak.Infrastructure.Services.SalaryServices.SalaryService;
using SummarySvc = Sehatak.Infrastructure.Services.FinancialSummaryService.FinancialSummaryService;

namespace Sehatak.IntegrationTests.Finance;

/// <summary>
/// Center with admin (C.AdminUserId), receptionist (Rec), doctor, patient + factories for the
/// expense / salary / summary services. Fixed past months are used so results never depend on "today".
/// Times given to helpers are clinic-local; they are stored as UTC like the app does.
/// </summary>
public abstract class FinanceTestBase(SqlServerFixture fx) : PaymentTestBase(fx)
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Hebron");

    protected int Admin => C.AdminUserId;

    protected ExpenseSvc NewExpenses() => new(Fx.Harness.NewSharedDb(), Fx.Harness.TenantFactory);
    protected SalarySvc NewSalaries() => new(Fx.Harness.NewSharedDb(), Fx.Harness.TenantFactory);
    protected SummarySvc NewSummary() => new(Fx.Harness.NewSharedDb(), Fx.Harness.TenantFactory, NewSalaries());

    /// <summary>Clinic-local date/time -> the UTC instant that is stored in the DB.</summary>
    protected static DateTime ClinicToUtc(DateOnly date, TimeOnly time) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified), Zone);

    protected async Task<int> AddExpenseRowAsync(DateOnly date, decimal amount, ExpenseSection section, string? name = null)
    {
        await using var db = Db();
        var e = new Expense { Name = name, Amount = amount, Section = section, ExpenseDate = date, CreatedByUserId = Admin };
        db.Expenses.Add(e);
        await db.SaveChangesAsync();
        return e.Id;
    }

    protected async Task AddEmergencyAsync(decimal amount, bool insured, DateTime createdUtc)
    {
        await using var db = Db();
        db.EmergencyCases.Add(new EmergencyCase
        {
            PatientName = "Emergency", DoctorUserId = Doc.UserId, ReceptionistId = Rec,
            IsInsurance = insured, AmountPaid = amount, CreatedAt = createdUtc, UpdatedAt = createdUtc
        });
        await db.SaveChangesAsync();
    }
}
