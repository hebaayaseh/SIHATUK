using Sehatak.Application.Common;
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
        Task<PagedResult<PaymentResponseDto>> GetPendingPaymentsAsync(PagedRequest request);
        Task<PagedResult<PaymentResponseDto>> GetCenterPaymentsAsync(int centerId,PagedRequest request);

    }
}
