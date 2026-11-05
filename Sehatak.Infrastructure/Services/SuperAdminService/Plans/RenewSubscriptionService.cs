using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.DTOs.RenewSubscription;
using Sehatak.Application.Interfaces.RenewSubscription;
using Sehatak.Domain.Entities.SharedEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;

namespace Sehatak.Infrastructure.Services.SuperAdminService.Plans
{
    public class RenewSubscriptionService : IRenewSubscription 
    {
        private readonly SharedDbContext sharedDbContext;
        private readonly TenantDbContextFactory contextFactory;
        public RenewSubscriptionService(SharedDbContext sharedDbContext,TenantDbContextFactory contextFactory)
        {
            this.sharedDbContext = sharedDbContext;
            this.contextFactory = contextFactory;
        }

        public async Task<bool> CancleSubscriptionAsync(int centarId, CancleSubcsriptionRequest request)
        {
            var center = await sharedDbContext.MedicalCenters
                .FindAsync(centarId);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centarId);
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == request.userId
                                                          && u.role == userRole.Admin);

            if (user == null)
                throw new BusinessException("Auth.Forbidden");

            var subscriptionPlan = await sharedDbContext.CenterSubscriptions
                .Include(p => p.Plan)
                .Where(u => u.CenterId == centarId && u.Status == SubscriptionStatus.Active
                 && u.Id == request.subscriptionId)
                 .FirstOrDefaultAsync();
            if (subscriptionPlan == null)
                throw new BusinessException("Subscription.PlanNotFound");

            subscriptionPlan.Status= SubscriptionStatus.Cancelled;
            center.CenterStatus = CenterStatus.Suspended;
            await sharedDbContext.SaveChangesAsync();

            return true;
        }

        public async Task<RenewSubscriptionResponseDto> RenewActiveSubscriptionAsync(int centerId, RenewSubscriptionRequest request)
        {
            var center = await sharedDbContext.MedicalCenters.FindAsync(centerId);
            if (center == null)
                throw new BusinessException("Center.NotFound");

            var currentSubscription = await sharedDbContext.CenterSubscriptions
                .OrderByDescending(c => c.EndDate)
                .FirstOrDefaultAsync(c => c.CenterId == centerId
                                       && c.Status == SubscriptionStatus.Active);

            if (currentSubscription == null)
                throw new BusinessException("Subscription.PlanNotFound");

            var pendingExists = await sharedDbContext.CenterSubscriptions
                .AnyAsync(c => c.CenterId == centerId && c.Status == SubscriptionStatus.Pending);
            if (pendingExists)
                throw new BusinessException("Subscription.Renewed"); 


            var newPlan = await sharedDbContext.SubscriptionPlans.FindAsync(request.newPlanId);
            if(newPlan == null || !newPlan.IsActive)
                throw new BusinessException("Subscription.PlanNotFound");

            
            var newSubscription = new CenterSubscription
            {
                CenterId = centerId,
                PlanId = request.newPlanId,
                Status = SubscriptionStatus.Pending,
                StartDate = currentSubscription.EndDate,
                EndDate = currentSubscription.EndDate.AddDays(newPlan.DurationDays),
                AmountPaid = newPlan.Price,
                
            };

            await sharedDbContext.CenterSubscriptions.AddAsync(newSubscription);
            await sharedDbContext.SaveChangesAsync();

            return new RenewSubscriptionResponseDto
            {
                SubscriptionId = newSubscription.Id,
                CenterId = centerId,
                PlanName = newPlan.Name,
                Amount = newPlan.Price,
                StartDate = newSubscription.StartDate,
                EndDate = newSubscription.EndDate,
                Status = newSubscription.Status,
                Message = "Subscription.RenewedPendingPayment"
            };
        }

        public async Task<RenewSubscriptionResponseDto> RenewExpiredSubscriptionAsync(int centerId, RenewSubscriptionRequest request)
        {
          
            var center = await sharedDbContext.MedicalCenters.FindAsync(centerId);
            if (center == null)
                throw new BusinessException("Center.NotFound");

            var expiredSubscription = await sharedDbContext.CenterSubscriptions
                .FirstOrDefaultAsync(c => c.CenterId == centerId
                                       && (c.Status == SubscriptionStatus.Expired
                                       || c.Status == SubscriptionStatus.Cancelled));

            if (expiredSubscription == null)
                throw new BusinessException("Subscription.PlanNotFound");

            var newPlan = await sharedDbContext.SubscriptionPlans.FindAsync(request.newPlanId);
            if (newPlan == null || !newPlan.IsActive)
                throw new BusinessException("Subscription.PlanNotFound");

            if (await sharedDbContext.CenterSubscriptions.AnyAsync(c => c.CenterId == centerId &&
                 (c.Status == SubscriptionStatus.Active || c.Status == SubscriptionStatus.Pending)))

                throw new BusinessException("Subscription.Renewed");

            var start = ClinicClock.Today;
            var newSubscription = new CenterSubscription
            {
                CenterId = centerId,
                PlanId = request.newPlanId,
                Status = SubscriptionStatus.Pending,  
                StartDate = start,
                EndDate = start.AddDays(newPlan.DurationDays),
                AmountPaid = newPlan.Price
            };

            await sharedDbContext.CenterSubscriptions.AddAsync(newSubscription);
            await sharedDbContext.SaveChangesAsync();

            return new RenewSubscriptionResponseDto
            {
                SubscriptionId = newSubscription.Id,
                CenterId = centerId,
                PlanName = newPlan.Name,
                Amount = newPlan.Price,
                StartDate = newSubscription.StartDate,
                EndDate = newSubscription.EndDate,
                Status = newSubscription.Status,
                Message = "Subscription.RenewedPendingPayment"
            };

        }
        public async Task<MySubscriptionResponseDto> GetMySubscriptionAsync(int centerId)
        {
            var center = await sharedDbContext.MedicalCenters.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == centerId);
            if (center == null)
                throw new BusinessException("Center.NotFound");

            var today = ClinicClock.Today;

            var subs = await sharedDbContext.CenterSubscriptions.AsNoTracking()
                .Include(s => s.Plan)
                .Where(s => s.CenterId == centerId)
                .ToListAsync();

            MySubscriptionItemDto? ToItem(CenterSubscription? s) => s == null ? null : new MySubscriptionItemDto
            {
                SubscriptionId = s.Id,
                PlanId = s.PlanId,
                PlanName = s.Plan.Name,
                Status = s.Status,
                StartDate = s.StartDate,
                EndDate = s.EndDate,
                DaysLeft = s.EndDate.DayNumber - today.DayNumber,
                Amount = s.AmountPaid
            };

            var actives = subs.Where(s => s.Status == SubscriptionStatus.Active).ToList();
            var current = actives.OrderBy(s => s.EndDate).FirstOrDefault();            
            var paidUntil = actives.Count == 0 ? (DateOnly?)null : actives.Max(s => s.EndDate);

            var pending = subs.Where(s => s.Status == SubscriptionStatus.Pending)
                .OrderByDescending(s => s.Id).FirstOrDefault();

            var lastEnded = subs.Where(s => s.Status == SubscriptionStatus.Expired || s.Status == SubscriptionStatus.Cancelled)
                .OrderByDescending(s => s.EndDate).ThenByDescending(s => s.Id).FirstOrDefault();

            var pendingHasPayment = pending != null && await sharedDbContext.subscriptionPayments
                .AnyAsync(p => p.SubscriptionId == pending.Id);

            string nextAction;
            if (pending != null)
                nextAction = pendingHasPayment ? "WaitingConfirmation" : "UploadPayment";
            else if (paidUntil != null)
                nextAction = paidUntil.Value.DayNumber - today.DayNumber <= 7 ? "RenewActive" : "None";
            else if (lastEnded != null)
                nextAction = "RenewExpired";
            else
                nextAction = "ContactSupport";

            return new MySubscriptionResponseDto
            {
                CenterId = centerId,
                CenterStatus = center.CenterStatus.ToString(),
                Current = ToItem(current),
                Pending = ToItem(pending),
                LastEnded = current == null ? ToItem(lastEnded) : null,
                PaidUntil = paidUntil,
                PendingHasPayment = pendingHasPayment,
                NextAction = nextAction
            };
        }

    }
}











  
