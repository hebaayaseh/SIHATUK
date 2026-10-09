using ClosedXML.Excel;
using Sehatak.Application.DTOs.FinancialReport;
using Sehatak.Application.DTOs.FinancialReportDto;
using Sehatak.Application.Interfaces.IFinancialReports;
using Sehatak.Application.Interfaces.IFinancialSummary;

public class FinancialReportAdminService : IFinancialReportAdmin
{
    private readonly IFinancialSummary financialSummary;

    public FinancialReportAdminService(IFinancialSummary financialSummary)
    {
        this.financialSummary = financialSummary;
    }

    private static readonly string[] ArabicDays =
            { "الأحد", "الاثنين", "الثلاثاء", "الأربعاء", "الخميس", "الجمعة", "السبت" };

    private static readonly Dictionary<string, string> CategoryLabels = new()
    {
        ["GeneralMedicine"] = "رواتب الطب العام",
        ["Lab"] = "رواتب المختبر",
        ["Nursing"] = "رواتب التمريض"
    };

    public async Task<byte[]> GenerateReportAsync(int centerId, int userId, FinancialReportRequestDto request)
    {
        var months = request.Month.HasValue
            ? new List<int> { request.Month.Value }
            : Enumerable.Range(1, 12).ToList();            // بدون شهر = شيت لكل شهر بالسنة

        using var workbook = new XLWorkbook();

        foreach (var month in months)
        {
            var summary = await financialSummary.GetMonthlySummaryAsync(
                centerId, userId, request.Year, month, includeDays: true);

            BuildMonthSheet(workbook, summary);
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void BuildMonthSheet(XLWorkbook workbook, MonthlySummaryDto s)
    {
        var ws = workbook.Worksheets.Add($"{s.Year}-{s.Month:D2}");
        ws.RightToLeft = true;

        var green = XLColor.FromHtml("#92D050");
        var yellow = XLColor.FromHtml("#FFD966");
        var blue = XLColor.FromHtml("#8EA9DB");

        // ===== الترويسة =====
        ws.Cell(2, 1).Value = "التاريخ";
        ws.Cell(2, 2).Value = "اليوم";

        WriteSectionHeader(ws, 3, "الطب العام", green);
        WriteSectionHeader(ws, 6, "المختبر", yellow);
        WriteSectionHeader(ws, 9, "عيادة الاختصاص", blue);

        ws.Cell(2, 12).Value = "المجموع اليومي النهائي";
        ws.Cell(2, 12).Style.Fill.BackgroundColor = green;
        ws.Cell(2, 13).Value = "فيزا";
        ws.Cell(2, 13).Style.Fill.BackgroundColor = yellow;

        // ===== الأيام =====
        var first = 3;
        var row = first;

        foreach (var d in s.Days)
        {
            ws.Cell(row, 1).Value = d.Date.ToDateTime(TimeOnly.MinValue);
            ws.Cell(row, 1).Style.DateFormat.Format = "d/M/yyyy";
            ws.Cell(row, 2).Value = ArabicDays[(int)d.Date.DayOfWeek];

            ws.Cell(row, 3).Value = d.GeneralMedicine.Expenses;
            ws.Cell(row, 4).Value = d.GeneralMedicine.Receipts;
            ws.Cell(row, 5).Value = d.GeneralMedicine.Total;

            ws.Cell(row, 6).Value = d.Lab.Expenses;
            ws.Cell(row, 7).Value = d.Lab.Receipts;
            ws.Cell(row, 8).Value = d.Lab.Total;

            ws.Cell(row, 9).Value = d.Specialty.Expenses;
            ws.Cell(row, 10).Value = d.Specialty.Receipts;
            ws.Cell(row, 11).Value = d.Specialty.Total;

            ws.Cell(row, 12).Value = d.DayTotal;
            ws.Cell(row, 13).Value = d.Lab.CardReceipts;   // لفيزا كل الأقسام: d.Lab.CardReceipts + d.Specialty.CardReceipts

            row++;
        }

        var totalRow = row;

        // ===== المجموع الشهري =====
        ws.Cell(totalRow, 2).Value = "المجموع الشهري";
        ws.Cell(totalRow, 3).Value = s.GeneralMedicine.Expenses;
        ws.Cell(totalRow, 4).Value = s.GeneralMedicine.Receipts;
        ws.Cell(totalRow, 5).Value = s.GeneralMedicine.Total;
        ws.Cell(totalRow, 6).Value = s.Lab.Expenses;
        ws.Cell(totalRow, 7).Value = s.Lab.Receipts;
        ws.Cell(totalRow, 8).Value = s.Lab.Total;
        ws.Cell(totalRow, 9).Value = s.Specialty.Expenses;
        ws.Cell(totalRow, 10).Value = s.Specialty.Receipts;
        ws.Cell(totalRow, 11).Value = s.Specialty.Total;
        ws.Cell(totalRow, 12).Value = s.MonthTotal;
        ws.Cell(totalRow, 13).Value = s.Lab.CardReceipts;

        var table = ws.Range(1, 1, totalRow, 13);
        table.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        table.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        table.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
        ws.Range(1, 1, 2, 13).Style.Font.Bold = true;
        ws.Range(totalRow, 1, totalRow, 13).Style.Font.Bold = true;

        // ===== بلوك حسابات الشهر =====
        var blockStart = totalRow + 2;
        ws.Cell(blockStart, 2).Value = "حسابات الشهر";
        ws.Cell(blockStart, 2).Style.Font.Bold = true;

        var r = blockStart + 1;

        AddBlockRow(ws, ref r, "المركز", s.CenterExpenses);

        foreach (var doc in s.Salaries.Doctors.Where(x => x.Amount > 0))
            AddBlockRow(ws, ref r, $"راتب د. {doc.DoctorName}", doc.Amount);

        foreach (var c in s.Salaries.Categories)
            AddBlockRow(ws, ref r, CategoryLabels.GetValueOrDefault(c.Category, c.Category), c.Amount);

        // ===== صافي حساب الشهر =====
        ws.Cell(r + 1, 2).Value = "صافي حساب الشهر";
        ws.Cell(r + 1, 3).Value = s.NetProfit;
        var net = ws.Range(r + 1, 2, r + 1, 3);
        net.Style.Fill.BackgroundColor = green;
        net.Style.Font.Bold = true;
        net.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;

        // للعلم فقط، خارج أي حساب
        ws.Cell(r + 3, 2).Value = "مقبوضات الطب العام بتأمين (للعلم)";
        ws.Cell(r + 3, 3).Value = s.GeneralInsuranceReceipts;

        ws.Column(1).Width = 12;
        ws.Column(2).Width = 28;
        ws.Columns(3, 13).Width = 13;
        ws.SheetView.FreezeRows(2);
    }

    private static void WriteSectionHeader(IXLWorksheet ws, int firstCol, string title, XLColor color)
    {
        var band = ws.Range(1, firstCol, 1, firstCol + 2);
        band.Merge();
        band.FirstCell().Value = title;
        band.Style.Fill.BackgroundColor = color;

        ws.Cell(2, firstCol).Value = "مصروفات";
        ws.Cell(2, firstCol + 1).Value = "مقبوضات";
        ws.Cell(2, firstCol + 2).Value = "المجموع";
        ws.Range(2, firstCol, 2, firstCol + 2).Style.Fill.BackgroundColor = color;
    }

    private static void AddBlockRow(IXLWorksheet ws, ref int row, string label, decimal value)
    {
        ws.Cell(row, 2).Value = label;
        ws.Cell(row, 3).Value = value;
        ws.Range(row, 2, row, 3).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        row++;
    }
}
