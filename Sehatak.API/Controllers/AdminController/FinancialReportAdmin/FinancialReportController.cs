using DocumentFormat.OpenXml.Bibliography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.DTOs.FinancialReport;
using Sehatak.Application.Interfaces.IFinancialReports;
using System.Security.Claims;

namespace Sehatak.API.Controllers.SuperAdminController1.FinancialReportAdmin
{
    [ApiController]
    [Route("[Controller]")]
    public class FinancialReportController : ControllerBase
    {
        private readonly IFinancialReportAdmin financialReport;
        public FinancialReportController(IFinancialReportAdmin financialReport)
        {
            this.financialReport = financialReport;
        }

        [Authorize(Policy = "AdminOnly")]
        [HttpGet("admin/{centerId}")]
        public async Task<IActionResult> FinancialReport(int centerId, [FromQuery] int year, [FromQuery] int? month)
        {
            int userId = int.Parse(
                User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

            var request = new FinancialReportRequestDto { Year = year, Month = month };

            var fileBytes = await financialReport.GenerateReportAsync(centerId,userId,request);

            var fileName = month.HasValue
                ? $"financial-report-{year}-{month:D1}.xlsx"
                : $"financial-report-{year}.xlsx";

            return File(
                fileBytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName
            );

        }
    }
}
