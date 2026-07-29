using Sehatak.Application.DTOs.ConsultationDto;
using Sehatak.Application.DTOs.PaymentDto;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.Interfaces.ConsultaionInterface
{
    public interface IConsultation
    {
        Task<List<DoctorEnableResponse>> GetDoctorEnableConsultation(int centerId);
        Task<string> ConsultationRequest(int centerId, int doctorId, int userId);
        Task<List<ConsultationResponse>> ViewConsultations(int centerId ,  int userId, ConsultationStatus status);
        Task<ConsultationResponse> ViewConsultation(int centerId, int doctorId, int userId);
        Task<string> ConsultationRecordPayment(int centerId, int consultationId, int userId , PaymentRequestDto request);
        Task<bool> ConfirmPaymentAsync(int centerId , int paymentId, int doctorId , DateTime ScheduledAt , string videoLink);
        Task<string> RejectConsultationRequestAsync(int centerId, int consultationId, int doctorId, string rejectionReason);
        Task<string> RejectConsultationPaymentAsync(int centerId, int paymentId, int doctorId, string rejectionReason);
        Task<List<PaymentResponseDto>> GetPaymentPinding(int centerId , int doctorId);
        Task<PaymentResponseDto> GetPaymentPinding(int centerId, int doctorId, int payment);
    }
}
