using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.CentersDto;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Domain.Enums.SharedEnums;

namespace Sehatak.Infrastructure.Services.SuperAdminService.CenterService
{
    public partial class centerService
    {
        /// <summary>اللاند بيج: كل المراكز الفعّالة، مع بحث اختياري بالاسم أو العنوان أو الهاتف.</summary>
        public async Task<PagedResult<PublicCenterDto>> SearchActiveCentersAsync(SearchCentersRequest request)
        {
            var query = sharedDbContext.MedicalCenters
                .AsNoTracking()
                .Where(c => c.CenterStatus == CenterStatus.Active);

            var term = request.Search?.Trim();
            if (!string.IsNullOrEmpty(term))
            {
                // رقم الهاتف: بنقارن بالأرقام بس (المستخدم ممكن يكتب 059-123 أو +970 59 123)
                var digits = new string(term.Where(char.IsDigit).ToArray());

                if (digits.Length >= 3)
                    query = query.Where(c =>
                        c.Name.Contains(term) ||
                        c.Address.Contains(term) ||
                        c.Phone.Contains(term) ||
                        c.Phone.Replace(" ", "").Replace("-", "").Replace("+", "").Contains(digits));
                else
                    query = query.Where(c =>
                        c.Name.Contains(term) ||
                        c.Address.Contains(term) ||
                        c.Phone.Contains(term));
            }

            var projected = query
                .OrderBy(c => c.Name)
                .ThenBy(c => c.Id)
                .Select(c => new PublicCenterDto
                {
                    Id = c.Id,
                    Name = c.Name,
                    UniqueUrl = c.UniqueUrl,
                    Phone = c.Phone,
                    Address = c.Address,
                    LogoUrl = c.LogoUrl
                });

            return await projected.ToPagedResultAsync(request.PageNumber, request.PageSize);
        }

        /// <summary>الأدمن بيشوف بيانات مركزه كاملة (حتى لو الاشتراك منتهي).</summary>
        public async Task<MyCenterResponseDto> GetMyCenterAsync(int centerId)
        {
            var center = await sharedDbContext.MedicalCenters
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == centerId);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            var subscription = await sharedDbContext.CenterSubscriptions
                .AsNoTracking()
                .Include(s => s.Plan)
                .Where(s => s.CenterId == centerId)
                .OrderByDescending(s => s.Status == SubscriptionStatus.Active)
                .ThenByDescending(s => s.EndDate)
                .FirstOrDefaultAsync();

            var features = new List<string>();
            if (subscription != null && subscription.Status == SubscriptionStatus.Active)
            {
                features = await sharedDbContext.PlanFeatures
                    .Where(p => p.PlanId == subscription.PlanId)
                    .Select(f => f.Feature.NameOfFeature)
                    .ToListAsync();
            }

            return new MyCenterResponseDto
            {
                Id = center.Id,
                Name = center.Name,
                UniqueUrl = center.UniqueUrl,
                Phone = center.Phone,
                Address = center.Address,
                LogoUrl = center.LogoUrl,
                AdminEmail = center.AdminEmail,
                AdminWhatsappNumber = center.AdminWhatsappNumber,
                BookingType = center.BookingType,
                RequiresPrepayment = center.RequiresPrepayment,
                PrepaymentAmount = center.PrepaymentAmount,
                RefundPolicyHours = center.RefundPolicyHours,
                PartialRefundPercent = center.PartialRefundPercent,
                CenterStatus = center.CenterStatus,
                CreatedAt = center.CreatedAt,
                PlanName = subscription?.Plan.Name,
                SubscriptionStatus = subscription?.Status,
                SubscriptionStartDate = subscription?.StartDate,
                SubscriptionEndDate = subscription?.EndDate,
                FeatureNames = features
            };
        }
    }
}
