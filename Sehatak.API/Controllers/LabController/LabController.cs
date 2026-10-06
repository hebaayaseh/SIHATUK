using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.LabDto;
using Sehatak.Application.Interfaces.ILab;

namespace Sehatak.API.Controllers.LabController
{
    [ApiController]
    [Route("[Controller]")]
    public class LabController : ControllerBase
    {
        private readonly ILab lab;
        public LabController(ILab lab)
        {
            this.lab = lab;
        }

        [Authorize(Policy = "DoctorOnly")]
        [HttpPost("doctor-create-lab-request/{centerId}")]
        public async Task<IActionResult> CreateLabRequest(int centerId, [FromBody] CreateLabRequestDto request)
        {
            var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            var result = await lab.CreateLabRequestAsync(centerId, userId, request);
            return Ok(result);
        }

        [Authorize(Policy = "DoctorOnly")]
        [HttpPut("doctor-update-lab-request/{centerId}")]
        public async Task<IActionResult> UpdateLabRequest(int centerId, [FromBody] UpdateLabRequestDto request)
        {
            var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            var result = await lab.UpdateLabRequestAsync(centerId, userId, request);
            return Ok(result);
        }

        [Authorize(Policy = "DoctorOnly")]
        [HttpGet("doctor-get-patient-lab-requests/{centerId}")]
        public async Task<IActionResult> DoctorGetLabRequestsAsync(int centerId, int patientId, [FromQuery] PagedRequest request)
        {
            var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            var result = await lab.GetLabRequestForPatientAsync(centerId, userId, patientId, request);
            return Ok(result);
        }

        [Authorize(Policy = "DoctorOnly")]
        [HttpGet("doctor-get-patient-lab-result/{centerId}")]
        public async Task<IActionResult> DoctorGetLabResultAsync(int centerId, int patientId, [FromQuery] int labRequestId)
        {
            var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            var result = await lab.DoctorGetLabResultsForPatient(centerId, userId, patientId, labRequestId);
            return Ok(result);
        }

        [Authorize(Policy = "PatientOnly")]
        [HttpGet("patient-get-lab-requests/{centerId}")]
        public async Task<IActionResult> PatientGetLabRequestsAsync(int centerId, [FromQuery] PagedRequest request, [FromQuery] int? subPatientId)
        {
            var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            var result = await lab.PatientGetLabRequestAsync(centerId, userId, request, subPatientId);
            return Ok(result);
        }

        [Authorize(Policy = "PatientOnly")]
        [HttpGet("patient-get-lab-results/{centerId}")]
        public async Task<IActionResult> PatientGetLabResultsAsync(int centerId, [FromQuery] PagedRequest request, [FromQuery] int? subPatientId)
        {
            var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            var result = await lab.PatientGetLabResultAsync(centerId, userId, request, subPatientId);
            return Ok(result);
        }

        [Authorize(Policy = "ReceptionistOnly")]
        [HttpPost("receptionist-create-patient-lab-requests/{centerId}")]
        public async Task<IActionResult> ReceptionistCreateLabRequestsAsync(int centerId, [FromBody] ReceptionistCreateLabRequestDto request)
        {
            var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            var result = await lab.ReceptionistCreateLabRequestAsync(centerId, userId, request);
            return Ok(result);
        }

        [Authorize(Policy = "ReceptionistOnly")]
        [HttpPut("receptionist-update-lab-requests/{centerId}")]
        public async Task<IActionResult> ReceptionistUpdateLabRequestsAsync(int centerId, [FromBody] ReceptionistUpdateLabRequestDto request)
        {
            var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            var result = await lab.ReceptionistUpdateLabRequestAsync(centerId, userId, request);
            return Ok(result);
        }

        [Authorize(Policy = "ReceptionistOnly")]
        [HttpGet("receptionist-get-lab-requests-await-payment/{centerId}")]
        public async Task<IActionResult> ReceptionistGetLabRequestsAwaitingPaymentAsync(int centerId, [FromQuery] PagedRequest request)
        {
            var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            var result = await lab.ReceptionistGetLabRequestsAwaitingPaymentAsync(centerId, userId, request);
            return Ok(result);
        }

        [Authorize(Policy = "LabRequest")]
        [HttpPut("receptionist-or-doctor-cancle-lab-requests/{centerId}/{labRequestId}")]
        public async Task<IActionResult> CancleLabRequestsAsync(int centerId,int labRequestId)
        {
            var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            var result = await lab.CancleLabRequestAsync(centerId, userId, labRequestId);
            return Ok(result);
        }

        [Authorize(Policy = "TechnicianOnly")]
        [HttpGet("technician-get-lab-pending-requests/{centerId}")]
        public async Task<IActionResult> TechnicianGetLabRequestsAsync(int centerId , [FromQuery] PagedRequest request)
        {
            var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            var result = await lab.LabGetPendingRequestsAsync(centerId,userId,request);
            return Ok(result);
        }

        [Authorize(Policy = "TechnicianOnly")]
        [HttpGet("technician-get-lab-request/{centerId}/{labRequestId}")]
        public async Task<IActionResult> TechnicianGetLabRequestAsync(int centerId, int labRequestId)
        {
            var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            var result = await lab.labGetRequestAsync(centerId, userId, labRequestId);
            return Ok(result);
        }

        [Authorize(Policy = "TechnicianOnly")]
        [HttpPut("technician-collected-lab-request/{centerId}/{labRequestId}")]
        public async Task<IActionResult> TechnicianCollectedLabRequestAsync(int centerId, int labRequestId,[FromBody] List<int>? unavailableItemIds)
        {
            var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            var result = await lab.LabCollectSample(centerId, userId, labRequestId,unavailableItemIds);
            return Ok(result);
        }

        [Authorize(Policy = "TechnicianOnly")]
        [HttpPut("technician-uplod-lab-request/{centerId}")]
        public async Task<IActionResult> TechnicianUplodLabRequestAsync(int centerId, [FromForm] UploadLabResultRequestDto request)
        {
            var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            var result = await lab.LabUploadResult(centerId, userId,request);
            return Ok(result);
        }
    }
}
