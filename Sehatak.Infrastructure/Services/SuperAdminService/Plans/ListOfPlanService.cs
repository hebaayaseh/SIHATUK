using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.AssignFeaturesWithPlan;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.DTOs.Plans;
using Sehatak.Application.DTOs.PlansDto;
using Sehatak.Application.Interfaces.Plans;
using Sehatak.Domain.Entities.SharedEntities;
using Sehatak.Infrastructure.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Infrastructure.Services.SuperAdminService.Plans
{
    public class ListOfPlanService : IPlan
    {
        private readonly SharedDbContext sharedDbContext;
        public ListOfPlanService(SharedDbContext sharedDbContext)
        {
            this.sharedDbContext = sharedDbContext;
        }

        public async Task<List<ListOfPlanResponseDto>> ListOfPlanAsync()
        {
            return await sharedDbContext.SubscriptionPlans
                .Where(p => p.IsActive)
                .Select(p => new ListOfPlanResponseDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    DurationDays = p.DurationDays,
                    Price = p.Price,
                    PlanFeatureId = p.PlanFeatures.Select(pf => pf.Feature.NameOfFeature).ToList()
                })
                .ToListAsync();
        }

        public async Task<EditRespondeDto> EditPlanAsync(int planId, EditPalnRequestDto request)
        {
            var Plan = await sharedDbContext.SubscriptionPlans
                .FirstOrDefaultAsync(p => p.Id == planId);

            if (Plan == null)
                throw new BusinessException("Subscription.PlanNotFound");


            if (request.price != null)
                Plan.Price = request.price.Value;

            if (request.name != null)
                Plan.Name = request.name;

            if (request.DurationDays != null)
                Plan.DurationDays = request.DurationDays.Value;

            await sharedDbContext.SaveChangesAsync();

            return new EditRespondeDto
            {
                Id = planId,
                price = Plan.Price,
                name = Plan.Name,
                DurationDays = Plan.DurationDays
            };

        }

        public async Task<PlanFeatureResponseDto> AssignFeatureAsync(int planId, AssignFeatureToPlanRequestDto request)
        {
            var plan = await sharedDbContext.SubscriptionPlans.FindAsync(planId);
            if (plan == null)
                throw new BusinessException("Subscription.PlanNotFound");

            var feature = await sharedDbContext.PlatformFeatures.FindAsync(request.featureId);
            if (feature == null)
                throw new BusinessException("General.NotFound");

            var alreadyLinked = await sharedDbContext.PlanFeatures.FirstOrDefaultAsync(p => p.PlanId == planId && p.FeatureId == request.featureId);
            if (alreadyLinked != null)
                throw new BusinessException("General.NotFound");

            var planFeature = new PlanFeature
            {
                PlanId = planId,
                FeatureId = request.featureId,
            };

            await sharedDbContext.PlanFeatures.AddAsync(planFeature);
            await sharedDbContext.SaveChangesAsync();

            return new PlanFeatureResponseDto
            {
                planId = planId,
                featureId = feature.Id,
                featureName = feature.NameOfFeature
            };

        }

        public async Task<List<PlanFeatureResponseDto>> GetPlanFeaturesAsync(int planId)
        {
            return await sharedDbContext.PlanFeatures
                .Where(pf => pf.PlanId == planId)
                .Select(pf => new PlanFeatureResponseDto
                {
                    planId = pf.PlanId,
                    featureId = pf.FeatureId,
                    featureName = pf.Feature.NameOfFeature
                })
                .ToListAsync();
        }

        public async Task<SubscriptionPlanResponseDto> AddSubscriptionPlan(SubscriptionPlanRequestDto request)
        {
            var supscriptionPlan = new SubscriptionPlan
            {
                Name = request.Name,
                DurationDays = request.DurationDays,
                Price = request.Price,

            };
            await sharedDbContext.SubscriptionPlans.AddAsync(supscriptionPlan);
            await sharedDbContext.SaveChangesAsync();

            return new SubscriptionPlanResponseDto
            {
                Id = supscriptionPlan.Id,
                Name = supscriptionPlan.Name,
                DurationDays = supscriptionPlan.DurationDays,
                Price = supscriptionPlan.Price
            };
        }


    }
}
