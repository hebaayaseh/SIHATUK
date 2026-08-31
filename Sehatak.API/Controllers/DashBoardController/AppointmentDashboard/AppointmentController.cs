using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.Interfaces.ApointmentInterface;
using Sehatak.Application.Interfaces.IDashBoard;
using Sehatak.Infrastructure.Services.AppointmentService;

namespace Sehatak.API.Controllers.DashBoard.AppointmentDashboard
{
    [ApiController]
    [Route("[Controller]")]
    public class AppointmentController : ControllerBase
    {
        private readonly IApointmentDashBoard dash;
        public AppointmentController(IApointmentDashBoard dash)
        {
            this.dash = dash;
        }
        [Authorize(Policy = "AdminOnly")]
        [HttpGet("appointments-summary/{centerId}")]
        public async Task<IActionResult> GetCenterAppointmentsSummary(int centerId, [FromQuery] DateOnly? date)
        {
            var result = await dash.GetCenterAppointmentsSummaryAsync(centerId, date);
            return Ok(result);
        }
    }
}
