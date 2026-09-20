using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.DTOs.RenewSubscription;
using Sehatak.Application.Interfaces.RenewSubscription;
using Sehatak.Domain.Entities.SharedEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;
using Sehatak.Application.Common;

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
                .FirstOrDefaultAsync(c => c.CenterId == centerId
                                       && c.Status == SubscriptionStatus.Active);

            if (currentSubscription == null)
                throw new BusinessException("Subscription.PlanNotFound");

            var pendingExists = await sharedDbContext.CenterSubscriptions
                .AnyAsync(c => c.CenterId == centerId && c.Status == SubscriptionStatus.Pending);
            if (pendingExists)
                throw new BusinessException("Subscription.Renewed"); 


            var newPlan = await sharedDbContext.SubscriptionPlans.FindAsync(request.newPlanId);
            if (newPlan == null)
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
                                       && c.Status == SubscriptionStatus.Expired);
            if (expiredSubscription == null)
                throw new BusinessException("Subscription.PlanNotFound");

            var newPlan = await sharedDbContext.SubscriptionPlans.FindAsync(request.newPlanId);
            if (newPlan == null || !newPlan.IsActive)
                throw new BusinessException("Subscription.PlanNotFound");

            var newSubscription = new CenterSubscription
            {
                CenterId = centerId,
                PlanId = request.newPlanId,
                Status = SubscriptionStatus.Pending,  
                StartDate = ClinicClock.Today,
                EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(newPlan.DurationDays)),
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

    }
}











  
