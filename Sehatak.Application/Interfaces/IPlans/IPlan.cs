using Sehatak.Application.Common;
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
        Task<PagedResult<ListOfPlanResponseDto>> ListOfPlanAsync(PagedRequest request);
        Task<EditRespondeDto> EditPlanAsync(int planId, EditPalnRequestDto request);
        Task<PlanFeatureResponseDto> AssignFeatureAsync(int planId, AssignFeatureToPlanRequestDto requst);
        Task<PagedResult<PlanFeatureResponseDto>> GetPlanFeaturesAsync(int planId, PagedRequest request);
        Task<SubscriptionPlanResponseDto> AddSubscriptionPlan(SubscriptionPlanRequestDto request);

    }
}
