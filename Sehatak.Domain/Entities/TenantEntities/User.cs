using Sehatak.Domain.Entities.General;
using Sehatak.Domain.Enums;
using System.ComponentModel.DataAnnotations;

namespace Sehatak.Domain.Entities.TenantEntities
{
    public class User
    {
        public int Id { get; set; }
        public string firstName {  get; set; }
        public string lastName { get; set; }
        [Required]
        [EmailAddress]   
        [MaxLength(150)]
        public string email { get; set; }
        [Required]
        [MinLength(8)]
        public string passwordHash { get; set; } = string.Empty;
        
        [Phone]         
        [MaxLength(20)]
        public string? phoneNumber { get; set; }
        public string address { get; set; }
        public string city { get; set; }
        public userRole role { get; set; }
        public DateTime createdAt { get; set; } = DateTime.UtcNow;
        public bool isActive { get; set; }
        public DateTime lastLogin { get; set; }
        public string? ProfileImageUrl { get; set; }
        public string? FcmToken { get; set; }

        // Navigation Properties :
        // use it because the Doctor and Pation have unique table 
        public Doctor? doctor { get; set; }

        public Patient? patient { get; set; }
        public ICollection<Notification> notification { get; set; } = new List<Notification>();
        public ICollection<Chat> sentMessages { get; set; } = new List<Chat>();
        public ICollection<Chat> receivedMessages {  get; set; } = new List<Chat>();
        public ICollection<StaffShift> Shifts { get; set; } = new List<StaffShift>();

    }
}
