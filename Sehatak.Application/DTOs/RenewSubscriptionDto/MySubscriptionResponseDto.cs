using Sehatak.Domain.Enums.SharedEnums;

namespace Sehatak.Application.DTOs.RenewSubscription
{
    /// <summary>What the center admin needs to see on the subscription screen (works for suspended centers too).</summary>
    public class MySubscriptionResponseDto
    {
        public int CenterId { get; set; }

        /// <summary>Pending / Active / Suspended</summary>
        public string CenterStatus { get; set; } = string.Empty;

        /// <summary>The running subscription (the Active one that ends first). Null when the center has none running.</summary>
        public MySubscriptionItemDto? Current { get; set; }

        /// <summary>A renewal requested but not confirmed yet.</summary>
        public MySubscriptionItemDto? Pending { get; set; }

        /// <summary>The latest Expired / Cancelled subscription (shown when nothing is running).</summary>
        public MySubscriptionItemDto? LastEnded { get; set; }

        /// <summary>The latest end date among Active subscriptions, i.e. until when the center is covered.</summary>
        public DateOnly? PaidUntil { get; set; }

        /// <summary>True when a payment proof was already uploaded for the pending subscription.</summary>
        public bool PendingHasPayment { get; set; }

        /// <summary>
        /// None | RenewActive | UploadPayment | WaitingConfirmation | RenewExpired | ContactSupport
        /// </summary>
        public string NextAction { get; set; } = "None";
    }

    public class MySubscriptionItemDto
    {
        public int SubscriptionId { get; set; }
        public int PlanId { get; set; }
        public string PlanName { get; set; } = string.Empty;
        public SubscriptionStatus Status { get; set; }
        public DateOnly StartDate { get; set; }
        public DateOnly EndDate { get; set; }
        public int DaysLeft { get; set; }
        public decimal Amount { get; set; }
    }
}
