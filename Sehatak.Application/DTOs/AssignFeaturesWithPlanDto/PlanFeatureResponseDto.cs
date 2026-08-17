using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.DTOs.AssignFeaturesWithPlan
{
    public class PlanFeatureResponseDto
    {
        public int featureId {  get; set; }
        public int planId { get; set; }
        public string featureName { get; set; }
    }
}
