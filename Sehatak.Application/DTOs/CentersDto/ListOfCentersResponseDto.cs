
namespace Sehatak.Application.DTOs.CentersDto
{
    public class ListOfCentersResponseDto
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string UniqueUrl { get; set; } = string.Empty;

        public string? Phone { get; set; }

        public string? Address { get; set; }

        public string? LogoUrl { get; set; }
        public int? AddedBySuperAdminId { get; set; }
        
        public string? AdminWhatsappNumber { get; set; }

    }
}
