using Sehatak.Application.Common;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;

namespace Sehatak.Application.DTOs.CentersDto
{
    public class SearchCentersRequest : PagedRequest
    {
        /// <summary>بحث بالاسم أو العنوان أو رقم الهاتف.</summary>
        public string? Search { get; set; }
    }

    /// <summary>اللي بينعرض للمريض في اللاند بيج: بيانات عامة فقط.</summary>
    public class PublicCenterDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string UniqueUrl { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string? Address { get; set; }
        public string? LogoUrl { get; set; }
    }

    /// <summary>بيانات المركز كاملة للأدمن نفسه.</summary>
    public class MyCenterResponseDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string UniqueUrl { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string? Address { get; set; }
        public string? LogoUrl { get; set; }
        public string? AdminEmail { get; set; }
        public string? AdminWhatsappNumber { get; set; }
        public BookingType BookingType { get; set; }
        public bool RequiresPrepayment { get; set; }
        public decimal PrepaymentAmount { get; set; }
        public int RefundPolicyHours { get; set; }
        public decimal PartialRefundPercent { get; set; }
        public CenterStatus CenterStatus { get; set; }
        public DateTime CreatedAt { get; set; }

        // الاشتراك (null إذا ما في اشتراك)
        public string? PlanName { get; set; }
        public SubscriptionStatus? SubscriptionStatus { get; set; }
        public DateOnly? SubscriptionStartDate { get; set; }
        public DateOnly? SubscriptionEndDate { get; set; }
        public List<string> FeatureNames { get; set; } = new();
    }
}
