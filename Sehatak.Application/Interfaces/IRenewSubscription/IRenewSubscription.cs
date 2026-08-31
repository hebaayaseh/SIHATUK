using Sehatak.Application.DTOs.RenewSubscription;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.Interfaces.RenewSubscription
{
    public interface IRenewSubscription
    {
        Task<RenewSubscriptionResponseDto> RenewActiveSubscriptionAsync(int centerId,RenewSubscriptionRequest request);
        Task<RenewSubscriptionResponseDto> RenewExpiredSubscriptionAsync(int centerId,RenewSubscriptionRequest request);
        Task<bool> CancleSubscriptionAsync(int centarId , CancleSubcsriptionRequest request);
    }
}
