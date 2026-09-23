using Sehatak.Application.Common;
using Sehatak.Application.DTOs.ConsultationDto;
using Sehatak.Application.DTOs.PaymentDto;
using Sehatak.Domain.Enums;

namespace Sehatak.Application.Interfaces.ConsultaionInterface
{
    public interface IConsultation
    {
        Task<PagedResult<DoctorEnableResponse>> GetDoctorEnableConsultation(int centerId, PagedRequest request);
        Task<string> ConsultationRequest(int centerId, int doctorId, int userId, int? subPatientId);
        Task<PagedResult<ConsultationResponse>> ViewConsultations(int centerId ,  int userId, ConsultationStatus status, PagedRequest request,int? subPatientId);
        Task<ConsultationResponse> ViewConsultation(int centerId, int doctorId, int userId, int consultationId, int? subPatientId);
        Task<string> ConsultationRecordPayment(int centerId, int consultationId, int userId , PaymentRequestDto request, int? subPatientId);
        Task<bool> ConfirmPaymentAsync(int centerId , int paymentId, int userId , string videoLink);
        Task<string> RejectConsultationRequestAsync(int centerId, int consultationId, int userId, string rejectionReason);
        Task<string> RejectConsultationPaymentAsync(int centerId, int paymentId, int userId, string rejectionReason);
        Task<PagedResult<PaymentResponseDto>> GetPaymentPinding(int centerId , int userId, PagedRequest request);
        Task<PaymentResponseDto> GetPaymentPinding(int centerId, int userId, int paymentId);
        Task<string> CancelConsultaion(int centerId, int userId, int consultationId,int? subPatientId);
        Task<string> CompleteConsultation(int centerId, int userId, int consultationId);
        Task<PagedResult<ConsultationResponseDto>> GetConsultationsScheduale(int centerId, int userId, PagedRequest request);
        Task<string> DoctorScheduleConsultationAsync(int centerId, int userId, int consultationId, DateTime scheduledAt);
    }
}
