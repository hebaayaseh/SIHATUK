
using System.ComponentModel.DataAnnotations;

namespace Sehatak.Application.DTOs.FeatureDto
{
    public class CreateFeatureRequestDto
    {
        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(300)]
        public string? Description { get; set; }
    }
}
