using Sehatak.Application.DTOs.RecordPaymentRequestDto;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.Interfaces.ISubscriptionPaymentService
{
    public interface ISubscriptionPayment
    {
        Task<PaymentResponseDto> RecordPaymentAsync(PaymentRequestExist request, int centerId);
        Task<bool> ConfirmPaymentAsync(int paymentId, int superAdminId);
        Task<List<PaymentResponseDto>> GetPendingPaymentsAsync();
        Task<List<PaymentResponseDto>> GetCenterPaymentsAsync(int centerId);

    }
}
