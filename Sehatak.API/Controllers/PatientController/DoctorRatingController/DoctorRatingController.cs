using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.DoctorRatingDto;
using Sehatak.Application.Interfaces.IDoctorRating;
using System.Security.Claims;

namespace Sehatak.API.Controllers.PatientController.DoctorRaitingController
{
    [ApiController]
    [Route("[Controller]")]
    public class DoctorRatingController : ControllerBase
    {
        private readonly IDoctorRating rating;
        public DoctorRatingController(IDoctorRating rating)
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

        [Authorize(Policy = "PatientOnly")]
        [HttpPost("patient-remove-doctor-rating/{centerId}/{ratingId}")]
        public async Task<IActionResult> RemoveDoctorRatingAsync(int centerId,int ratingId)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await rating.RemoveDoctorRatingAsync(centerId, userId, ratingId);
            return Ok(result);
        }

        [Authorize(Policy = "PatientOnly")]
        [HttpPost("patient-get-ratings/{centerId}")]
        public async Task<IActionResult> PatientGetRatingsAsync(int centerId, [FromQuery] PagedRequest request)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await rating.PatientGetRatingsAsync(centerId, userId,request);
            return Ok(result);
        }

        [Authorize(Policy = "DoctorOnly")]
        [HttpPost("doctor-get-ratings/{centerId}")]
        public async Task<IActionResult> DoctorGetRatingsAsync(int centerId)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await rating.DoctorGetRatingsAsync(centerId, userId);
            return Ok(result);
        }

    }
}
