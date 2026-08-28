using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.DTOs.SearchDoctorDto;
using Sehatak.Application.Interfaces.SearchDoctor;

namespace Sehatak.API.Controllers.PatientController.SearchDoctor
{
    [ApiController]
    [Route("api/[Controller]")]
    public class SearchDoctorControlle : ControllerBase
    {
        private readonly ISearchDoctor searchDoctor;
        public SearchDoctorControlle(ISearchDoctor searchDoctor)
        {
            this.searchDoctor = searchDoctor;
        }
        [HttpPost("search-doctor-name/{centerId}")]
        public async Task<IActionResult> SearchDoctor(int centerId , [FromBody] SearchDoctorRequest request)
        {
            var result = await searchDoctor.SearchDoctorAsync(centerId, request);
            return Ok(result);
        }
    }
}
