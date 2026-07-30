using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.DTOs.ConsultationDto;
using Sehatak.Application.DTOs.PaymentDto;
using Sehatak.Application.Interfaces.ConsultaionInterface;
using Sehatak.Domain.Enums;
using System.Security.Claims;

namespace Sehatak.API.Controllers.Consultationcontroller
{
    [ApiController]
    [Route("Consultation")]
    public class ConsultationController : ControllerBase
    {
        private readonly IConsultation consultation;
        public ConsultationController(IConsultation consultation)
        {
            this.consultation = consultation;
        }


        [HttpGet("get-doctors/{centerId}")]
        public async Task<IActionResult> GetDoctors(int centerId)
        {
            var result = await consultation.GetDoctorEnableConsultation(centerId);
            return Ok(result);
        }


        [Authorize(Policy = "PatientOnly")]
        [HttpPost("request/{centerId}/{doctorId}")]
        public async Task<IActionResult> RequestConsultation(int centerId, int doctorId)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await consultation.ConsultationRequest(centerId, doctorId, userId);
            return Ok(result);
        }

        [Authorize(Policy = "PatientOnly")]
        [HttpGet("get-consultation/{centerId}/{doctorId}")]
        public async Task<IActionResult> GetConsultation(int centerId, int doctorId)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await consultation.ViewConsultation(centerId, doctorId, userId);
            return Ok(result);
        }

        [Authorize(Policy = "PatientOnly")]
        [HttpPost("get-consultations/{centerId}")]
        public async Task<IActionResult> GetConsultations(int centerId, [FromBody] ConsultationStatus status)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await consultation.ViewConsultations(centerId, userId, status);
            return Ok(result);
        }

        [Authorize(Policy = "PatientOnly")]
        [HttpPost("record-payment/{centerId}/{consultationId}")]
        public async Task<IActionResult> ConsultationRecordPaymentAsync(int centerId, int consultationId, [FromBody] PaymentRequestDto request)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await consultation.ConsultationRecordPayment(centerId, consultationId, userId, request);
            return Ok(result);
        }


        [Authorize(Policy = "DoctorOnly")]
        [HttpPost("confirm-payment/{centerId}/{paymentId}")]
        public async Task<IActionResult> ConfirmPayment(int centerId, int paymentId, [FromBody] ConfirmPaymentRequest request)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await consultation.ConfirmPaymentAsync(centerId, paymentId, userId, request.ScheduledAt, request.VideoLink);
            return Ok(result);
        }

        [Authorize(Policy = "DoctorOnly")]
        [HttpPost("reject-payment/{centerId}/{paymentId}")]
        public async Task<IActionResult> RejectConsultationPayment(int centerId, int paymentId, [FromBody] RejectReasonRequest request)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await consultation.RejectConsultationPaymentAsync(centerId, paymentId, userId, request.Reason);
            return Ok(result);
        }

        [Authorize(Policy = "DoctorOnly")]
        [HttpPost("reject-request/{centerId}/{consultationId}")]
        public async Task<IActionResult> RejectConsultationRequest(int centerId, int consultationId, [FromBody] RejectReasonRequest request)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await consultation.RejectConsultationRequestAsync(centerId, consultationId, userId, request.Reason);
            return Ok(result);
        }

        [Authorize(Policy = "DoctorOnly")]
        [HttpGet("pending-payments/{centerId}")]
        public async Task<IActionResult> GetPendingPayments(int centerId)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await consultation.GetPaymentPinding(centerId, userId);
            return Ok(result);
        }

        [Authorize(Policy = "DoctorOnly")]
        [HttpGet("pending-payments/{centerId}/{paymentId}")]
        public async Task<IActionResult> GetPendingPayment(int centerId, int paymentId)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await consultation.GetPaymentPinding(centerId, userId, paymentId);
            return Ok(result);
        }

        [Authorize(Policy = "PatientOnly")]
        [HttpPut("cancel/{centerId}/{consultationId}")]
        public async Task<IActionResult> CancelConsultationAsync(int centerId , int consultationId)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await consultation.CancelConsultaion(centerId, userId, consultationId);
            return Ok(result);
        }

        [Authorize(Policy = "DoctorOnly")]
        [HttpGet("complete-consultation/{centerId}/{consultationId}")]
        public async Task<IActionResult> CompleteConsultationAsync(int centerId, int consultationId)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await consultation.CompleteConsultation(centerId, userId, consultationId);
            return Ok(result);
        }

    }

}