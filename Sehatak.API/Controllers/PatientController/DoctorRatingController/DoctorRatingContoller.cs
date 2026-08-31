using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.DTOs.DoctorRaitingDto;
using Sehatak.Application.DTOs.DoctorRatingDto;
using Sehatak.Application.Interfaces.IDoctorRating;
using System.Security.Claims;

namespace Sehatak.API.Controllers.PatientController.DoctorRaitingController
{
    [ApiController]
    [Route("[Controller]")]
    public class DoctorRatingContoller : ControllerBase
    {
        private readonly IDoctorRating rating;
        public DoctorRatingContoller(IDoctorRating rating)
        {
            this.rating = rating;
        }

        [Authorize(Policy = "PatientOnly")]
        [HttpPost("patient-add-doctor-rating/{centerId}")]
        public async Task<IActionResult> AddDoctorRatingAsync(int centerId , [FromBody]AddDoctorRatingRequest request)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await rating.AddDoctorRatingAsync(centerId, userId, request);
            return Ok(result);
        }

        [Authorize(Policy = "PatientOnly")]
        [HttpPost("patient-update-doctor-rating/{centerId}")]
        public async Task<IActionResult> UpdateDoctorRatingAsync(int centerId, [FromBody] UpdateDoctorRatingRequest request)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await rating.UpdateDoctorRatingAsync(centerId, userId, request);
            return Ok(result);
        }

    }
}
