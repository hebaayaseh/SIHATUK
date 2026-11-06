using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.DTOs.SearchDoctorDto;
using Sehatak.Application.Interfaces.SearchDoctor;

namespace Sehatak.API.Controllers.PatientController.SearchDoctor
{
    [ApiController]
    [Route("[Controller]")]
    public class SearchDoctorControlle : ControllerBase
    {
        private readonly ISearchDoctor searchDoctor;
        public SearchDoctorControlle(ISearchDoctor searchDoctor)
        {
            this.searchDoctor = searchDoctor;
        }
        [HttpGet("search-doctor/{centerId}")]
        [AllowAnonymous]
        public async Task<IActionResult> SearchDoctor(int centerId , [FromQuery] SearchDoctorRequest request)
        {
            var result = await searchDoctor.SearchDoctorAsync(centerId, request);
            return Ok(result);
        }
    }
}
