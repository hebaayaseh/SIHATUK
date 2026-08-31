using Sehatak.Application.DTOs.AssignFeaturesWithPlan;
using Sehatak.Application.DTOs.Plans;
using Sehatak.Application.DTOs.PlansDto;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.Interfaces.Plans
{
    public interface IPlan
    {
        Task<List<ListOfPlanResponseDto>> ListOfPlanAsync();
        Task<EditRespondeDto> EditPlanAsync(int planId, EditPalnRequestDto request);
        Task<PlanFeatureResponseDto> AssignFeatureAsync(int planId, AssignFeatureToPlanRequestDto requst);
        Task<List<PlanFeatureResponseDto>> GetPlanFeaturesAsync(int planId);
        Task<SubscriptionPlanResponseDto> AddSubscriptionPlan(SubscriptionPlanRequestDto request);

    }
}
