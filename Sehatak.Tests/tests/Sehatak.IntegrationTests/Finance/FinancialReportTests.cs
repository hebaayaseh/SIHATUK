using ClosedXML.Excel;
using Sehatak.Application.DTOs.FinancialReport;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.PaymentEnums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;
using SuperAdminReport = Sehatak.Infrastructure.Services.FinancialReportServices.FinancialReportService;

namespace Sehatak.IntegrationTests.Finance;

/// <summary>Center admin's Excel report (built from the monthly summary) + the SuperAdmin subscriptions report guards.</summary>
[Collection(SqlServerCollection.Name)]
public class FinancialReportTests(SqlServerFixture fx) : FinanceTestBase(fx)
{
    private FinancialReportAdminService NewReport() => new(NewSummary());

    private static XLWorkbook Open(byte[] bytes) => new(new MemoryStream(bytes));

    [Fact]
    public async Task Month_report_has_one_sheet_with_a_row_per_day_and_a_total_row()
    {
        await AddExpenseRowAsync(new DateOnly(2025, 1, 5), 25m, ExpenseSection.Lab);

        using var wb = Open(await NewReport().GenerateReportAsync(C.Id, Admin, new FinancialReportRequestDto { Year = 2025, Month = 1 }));

        wb.Worksheets.Count.ShouldBe(1);
        var ws = wb.Worksheet(1);
        ws.Name.ShouldBe("2025-01");
        ws.Cell(3, 6).GetValue<decimal>().ShouldBe(0m);               // day 1, Lab expenses
        ws.Cell(7, 6).GetValue<decimal>().ShouldBe(25m);              // day 5 (row 3 + 4), Lab expenses
        ws.Cell(3 + 31, 2).GetString().ShouldBe("المجموع الشهري");     // total row right after 31 days
    }

    [Fact]
    public async Task Year_report_has_a_sheet_per_month()
    {
        using var wb = Open(await NewReport().GenerateReportAsync(C.Id, Admin, new FinancialReportRequestDto { Year = 2025 }));
        wb.Worksheets.Count.ShouldBe(12);
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Visa_column_counts_card_payments_of_all_sections()
    {
        var noon = new TimeOnly(12, 0);
        await AddPaymentAsync(Pat.PatientId, 100m, PaymentType.Appointment, PaymentStatus.Paid, method: PaymentMethod.card, paidAtUtc: ClinicToUtc(new DateOnly(2025, 1, 2), noon));
        await AddPaymentAsync(Pat.PatientId, 60m, PaymentType.Lab, PaymentStatus.Paid, method: PaymentMethod.card, paidAtUtc: ClinicToUtc(new DateOnly(2025, 1, 2), noon));

        using var wb = Open(await NewReport().GenerateReportAsync(C.Id, Admin, new FinancialReportRequestDto { Year = 2025, Month = 1 }));
        var ws = wb.Worksheet(1);

        ws.Cell(4, 13).GetValue<decimal>().ShouldBe(160m);            // day 2
        ws.Cell(3 + 31, 13).GetValue<decimal>().ShouldBe(160m);       // monthly total
    }

    [Fact]
    public async Task Non_admin_cannot_download_the_report() =>
        await ShouldFailWith("Auth.Forbidden", () => NewReport().GenerateReportAsync(C.Id, Rec, new FinancialReportRequestDto { Year = 2025, Month = 1 }));

    // ---------- SuperAdmin report guards (no DB needed: validation happens first) ----------

    [Theory]
    [InlineData(0, null)]
    [InlineData(-1, null)]
    [InlineData(2025, 0)]
    [InlineData(2025, 13)]
    [Trait("Category", "KnownBug")]
    public async Task SuperAdmin_report_with_a_bad_year_or_month_gives_a_business_error(int year, int? month)
    {
        var svc = new SuperAdminReport(Fx.Harness.NewSharedDb());
        await Should.ThrowAsync<BusinessException>(() => svc.GenerateReportAsync(new FinancialReportRequestDto { Year = year, Month = month }));
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task SuperAdmin_report_for_an_empty_period_is_a_valid_workbook_with_both_sheets()
    {
        var svc = new SuperAdminReport(Fx.Harness.NewSharedDb());
        using var wb = Open(await svc.GenerateReportAsync(new FinancialReportRequestDto { Year = 2001, Month = 3 }));

        wb.Worksheets.Count.ShouldBe(2);
        wb.Worksheet(2).Cell(1, 1).GetString().ShouldBe("Center");   // header must be on row 1
    }
}
