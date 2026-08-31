
using Sehatak.Application.DTOs.CreateCenterRequestDto;
using Sehatak.Application.DTOs.RecordPaymentRequestDto;

namespace Sehatak.Application.Interfaces.CenterRegistrationRequest
{
    public interface ICenterRegistration
    {
        Task<CenterRegistrationResponseDto> CenterRequestAsync(CenterRegistrationRequestDto request);
        Task<List<CenterRegistrationResponseDto>> GetCentersRegisterationAsync();
        Task<CenterRegistrationResponseDto?> GetCenterRegistrationAsync(int centerId);
        Task<bool> ApproveCenterRequest(int requestId, int superAdminId);
        Task<bool> RejectAsync(int requestId, int superAdminId, string rejectionReason);

        Task<bool> RecordRegistrationPaymentAsync(int requestId, recordPaymentRequestDto request);
        Task<List<PaymentResponseDto>> GetPendingRegistrationPaymentsAsync();
        Task<bool> ConfirmRegistrationPaymentAsync(int paymentId, int superAdminId);
    }
}