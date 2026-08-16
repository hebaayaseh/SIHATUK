using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.DTOs.FinancialReport;
using Sehatak.Application.Interfaces.IFinancialReports;

namespace Sehatak.API.Controllers.SuperAdminController.FinancialReport
{
    [ApiController]
    [Route("api/SuperAdminOnly")]
    public class FinancialReportController : ControllerBase
    {
        private readonly IFinancialReport financialReport;
        public FinancialReportController(IFinancialReport financialReport)
        {
            this.financialReport = financialReport;
        }
        [Authorize(Policy = "SuperAdminOnly")]
        [HttpGet("financial-report")]
        public async Task<IActionResult> ExportReport([FromQuery] int year, [FromQuery] int? month)
        {
            var request = new FinancialReportRequestDto { Year = year, Month = month };
            var fileBytes = await financialReport.GenerateReportAsync(request);

            var fileName = month.HasValue
                ? $"financial-report-{year}-{month:D2}.xlsx"
                : $"financial-report-{year}.xlsx";

            return File(
                fileBytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName
            );

        }
    }
}
