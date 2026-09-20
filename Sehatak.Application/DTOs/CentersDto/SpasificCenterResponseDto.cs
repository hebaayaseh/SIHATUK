
namespace Sehatak.Application.DTOs.CentersDto
{
    public class SpasificCenterResponseDto
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string UniqueUrl { get; set; } = string.Empty;

        public string? Phone { get; set; }

        public string? Address { get; set; }

        public string? LogoUrl { get; set; }
        public int? AddedBySuperAdminId { get; set; }

        public string? AdminWhatsappNumber { get; set; }
        public string? PlanName { get; set; } 
        public List<string> FeatureNames { get; set; } = new List<string>();

    }
}
