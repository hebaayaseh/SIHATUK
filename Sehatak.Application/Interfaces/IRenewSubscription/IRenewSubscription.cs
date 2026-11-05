using Sehatak.Application.DTOs.RenewSubscription;

namespace Sehatak.Application.Interfaces.RenewSubscription
{
    public interface IRenewSubscription
    {
        Task<RenewSubscriptionResponseDto> RenewActiveSubscriptionAsync(int centerId,RenewSubscriptionRequest request);
        Task<RenewSubscriptionResponseDto> RenewExpiredSubscriptionAsync(int centerId,RenewSubscriptionRequest request);
        Task<bool> CancleSubscriptionAsync(int centarId , CancleSubcsriptionRequest request);
        Task<MySubscriptionResponseDto> GetMySubscriptionAsync(int centerId);
    }
}
