using Sehatak.Application.DTOs.MonthlySalaryDto;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.PaymentEnums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Finance;

[Collection(SqlServerCollection.Name)]
public class FinancialSummaryTests(SqlServerFixture fx) : FinanceTestBase(fx)
{
    private const int Y = 2025, M = 1;                        // January 2025 (31 days, winter time)
    private static readonly TimeOnly Noon = new(12, 0);        // mid-day so the tests do not depend on DST handling
    private static DateOnly Day(int d, int month = M) => new(Y, month, d);

    private Task<Sehatak.Application.DTOs.FinancialReportDto.MonthlySummaryDto> Summary(bool days = true, int month = M) =>
        NewSummary().GetMonthlySummaryAsync(C.Id, Admin, Y, month, days);

    private Task PayAsync(decimal amount, PaymentType type, int day, PaymentStatus status = PaymentStatus.Paid, PaymentMethod method = PaymentMethod.cash, int month = M) =>
        AddPaymentAsync(Pat.PatientId, amount, type, status, method: method, paidAtUtc: ClinicToUtc(Day(day, month), Noon));

    // ---------- shape ----------

    [Fact]
    public async Task Empty_month_has_one_zero_row_per_day()
    {
        var s = await Summary();
        s.Days.Count.ShouldBe(31);
        s.Days.First().Date.ShouldBe(Day(1));
        s.Days.Last().Date.ShouldBe(Day(31));
        s.MonthTotal.ShouldBe(0m);
        s.NetProfit.ShouldBe(0m);
    }

    [Fact]
    public async Task February_has_28_days_in_2025_and_29_in_2024()
    {
        (await NewSummary().GetMonthlySummaryAsync(C.Id, Admin, 2025, 2, true)).Days.Count.ShouldBe(28);
        (await NewSummary().GetMonthlySummaryAsync(C.Id, Admin, 2024, 2, true)).Days.Count.ShouldBe(29);
    }

    [Fact]
    public async Task Days_are_left_out_when_not_requested_but_totals_stay()
    {
        await PayAsync(100m, PaymentType.Appointment, 5);
        var s = await Summary(days: false);
        s.Days.ShouldBeEmpty();
        s.Specialty.Receipts.ShouldBe(100m);
    }

    // ---------- receipts ----------

    [Fact]
    public async Task Appointment_consultation_and_prepayment_go_to_Specialty_lab_goes_to_Lab()
    {
        await PayAsync(100m, PaymentType.Appointment, 5);
        await PayAsync(50m, PaymentType.Consultation, 5);
        await PayAsync(30m, PaymentType.Prepayment, 6);
        await PayAsync(70m, PaymentType.Lab, 5);

        var s = await Summary();
        s.Specialty.Receipts.ShouldBe(180m);
        s.Lab.Receipts.ShouldBe(70m);
        s.GeneralMedicine.Receipts.ShouldBe(0m);
        s.Days.Single(d => d.Date == Day(5)).Specialty.Receipts.ShouldBe(150m);
        s.Days.Single(d => d.Date == Day(6)).Specialty.Receipts.ShouldBe(30m);
    }

    [Theory]
    [InlineData(PaymentStatus.Pending)]
    [InlineData(PaymentStatus.Failed)]
    [InlineData(PaymentStatus.Refunded)]
    public async Task Only_Paid_payments_count(PaymentStatus status)
    {
        await PayAsync(100m, PaymentType.Appointment, 5, status);
        (await Summary()).Specialty.Receipts.ShouldBe(0m);
    }

    [Theory]
    [InlineData(PaymentType.Refund)]
    [InlineData(PaymentType.Emergency)]
    public async Task Refund_and_Emergency_payment_rows_are_not_receipts(PaymentType type)
    {
        await PayAsync(100m, type, 5);
        var s = await Summary();
        s.Specialty.Receipts.ShouldBe(0m);
        s.Lab.Receipts.ShouldBe(0m);
    }

    [Fact]
    public async Task Card_receipts_are_tracked_per_section()
    {
        await PayAsync(100m, PaymentType.Appointment, 5, method: PaymentMethod.card);
        await PayAsync(40m, PaymentType.Appointment, 5, method: PaymentMethod.cash);
        await PayAsync(60m, PaymentType.Lab, 5, method: PaymentMethod.card);

        var s = await Summary();
        s.Specialty.CardReceipts.ShouldBe(100m);
        s.Lab.CardReceipts.ShouldBe(60m);
    }

    [Fact]
    public async Task Payments_of_other_months_are_ignored_including_the_edge_days()
    {
        await PayAsync(10m, PaymentType.Appointment, 31, month: 12);   // December 2025: never in January
        await PayAsync(20m, PaymentType.Appointment, 1, month: 2);
        await PayAsync(5m, PaymentType.Appointment, 1);
        await PayAsync(7m, PaymentType.Appointment, 31);

        var s = await Summary();
        s.Specialty.Receipts.ShouldBe(12m);
        s.Days.First().Specialty.Receipts.ShouldBe(5m);
        s.Days.Last().Specialty.Receipts.ShouldBe(7m);
    }

    // ---------- emergency ----------

    [Fact]
    public async Task Emergency_cases_count_as_GeneralMedicine_receipts_and_insured_ones_are_also_tracked_separately()
    {
        await AddEmergencyAsync(100m, insured: false, ClinicToUtc(Day(9), Noon));
        await AddEmergencyAsync(40m, insured: true, ClinicToUtc(Day(9), Noon));

        var s = await Summary();
        s.GeneralMedicine.Receipts.ShouldBe(140m);
        s.GeneralInsuranceReceipts.ShouldBe(40m);
        s.Days.Single(d => d.Date == Day(9)).GeneralMedicine.Receipts.ShouldBe(140m);
    }

    // ---------- expenses / salaries / net ----------

    [Fact]
    public async Task Expenses_are_split_by_section_and_day()
    {
        await AddExpenseRowAsync(Day(3), 10m, ExpenseSection.GeneralMedicine);
        await AddExpenseRowAsync(Day(3), 20m, ExpenseSection.Lab);
        await AddExpenseRowAsync(Day(4), 30m, ExpenseSection.Specialty);
        await AddExpenseRowAsync(Day(4), 40m, ExpenseSection.Center);
        await AddExpenseRowAsync(Day(1).AddMonths(1), 99m, ExpenseSection.Center);   // February: ignored

        var s = await Summary();
        s.GeneralMedicine.Expenses.ShouldBe(10m);
        s.Lab.Expenses.ShouldBe(20m);
        s.Specialty.Expenses.ShouldBe(30m);
        s.CenterExpenses.ShouldBe(40m);
        s.Days.Single(d => d.Date == Day(4)).CenterExpenses.ShouldBe(40m);
    }

    [Fact]
    public async Task Net_profit_is_receipts_minus_section_expenses_minus_center_expenses_minus_salaries()
    {
        await PayAsync(1000m, PaymentType.Appointment, 5);
        await PayAsync(500m, PaymentType.Lab, 5);
        await AddEmergencyAsync(200m, false, ClinicToUtc(Day(5), Noon));
        await AddExpenseRowAsync(Day(5), 100m, ExpenseSection.Specialty);
        await AddExpenseRowAsync(Day(5), 50m, ExpenseSection.Center);
        await NewSalaries().SetCategorySalaryAsync(C.Id, Admin, new SetCategorySalaryRequestDto { Year = Y, Month = M, Category = SalaryCategory.Nursing, Amount = 300m });
        await NewSalaries().SetDoctorSalaryAsync(C.Id, Admin, new SetDoctorSalaryRequestDto { DoctorId = Doc.DoctorId, Year = Y, Month = M, Amount = 400m });

        var s = await Summary();

        s.MonthTotal.ShouldBe(1000m + 500m + 200m - 100m);          // section totals (receipts - section expenses)
        s.Salaries.TotalSalaries.ShouldBe(700m);
        s.NetProfit.ShouldBe(1600m - 50m - 700m);
        s.MonthTotal.ShouldBe(s.Days.Sum(d => d.DayTotal));
    }

    // ---------- clinic day boundaries ----------

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Payment_just_after_midnight_in_summer_belongs_to_the_clinic_day_not_the_utc_day()
    {
        // 00:30 clinic time on 11 May 2025 = 21:30 UTC on 10 May (UTC+3). Wrong whenever the server's "current" offset is +2.
        await AddPaymentAsync(Pat.PatientId, 100m, PaymentType.Appointment, PaymentStatus.Paid,
            paidAtUtc: ClinicToUtc(new DateOnly(2025, 5, 11), new TimeOnly(0, 30)));

        var s = await NewSummary().GetMonthlySummaryAsync(C.Id, Admin, 2025, 5, true);
        s.Days.Single(d => d.Date == new DateOnly(2025, 5, 11)).Specialty.Receipts.ShouldBe(100m);
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Payment_late_at_night_in_winter_belongs_to_the_clinic_day()
    {
        // 23:30 clinic time on 31 Jan 2025 = 21:30 UTC (UTC+2). Wrong whenever the server's "current" offset is +3.
        await AddPaymentAsync(Pat.PatientId, 100m, PaymentType.Appointment, PaymentStatus.Paid,
            paidAtUtc: ClinicToUtc(Day(31), new TimeOnly(23, 30)));

        var s = await Summary();
        s.Days.Single(d => d.Date == Day(31)).Specialty.Receipts.ShouldBe(100m);
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Payment_after_midnight_on_the_first_belongs_to_the_new_month()
    {
        // 00:30 clinic time on 1 June 2025 = 21:30 UTC on 31 May. It must be in June, not May.
        await AddPaymentAsync(Pat.PatientId, 100m, PaymentType.Appointment, PaymentStatus.Paid,
            paidAtUtc: ClinicToUtc(new DateOnly(2025, 6, 1), new TimeOnly(0, 30)));

        (await NewSummary().GetMonthlySummaryAsync(C.Id, Admin, 2025, 5, false)).Specialty.Receipts.ShouldBe(0m);
        (await NewSummary().GetMonthlySummaryAsync(C.Id, Admin, 2025, 6, false)).Specialty.Receipts.ShouldBe(100m);
    }

    // ---------- guards ----------

    [Theory]
    [InlineData(0, 5, "Validation.InvalidYear")]
    [InlineData(2025, 0, "Validation.InvalidMonth")]
    [InlineData(2025, 13, "Validation.InvalidMonth")]
    public Task Bad_year_or_month_is_rejected(int year, int month, string code) =>
        ShouldFailWith(code, () => NewSummary().GetMonthlySummaryAsync(C.Id, Admin, year, month, false));

    [Fact]
    public async Task Only_an_admin_of_an_active_center_can_read_it()
    {
        await ShouldFailWith("Auth.Forbidden", () => NewSummary().GetMonthlySummaryAsync(C.Id, Rec, Y, M, false));
        await ShouldFailWith("Center.NotFound", () => NewSummary().GetMonthlySummaryAsync(999_999, Admin, Y, M, false));
    }
}
