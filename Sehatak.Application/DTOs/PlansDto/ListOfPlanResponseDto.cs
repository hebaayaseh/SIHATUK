

namespace Sehatak.Application.DTOs.PlansDto
{
    public class ListOfPlanResponseDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;

        public decimal Price { get; set; }

        public int DurationDays { get; set; }
        public List<string> PlanFeatureNames { get; set; }
    }
}
