

using Microsoft.EntityFrameworkCore.Storage.ValueConversion.Internal;

namespace Sehatak.Application.DTOs.MedicationReminderDto
{
    public class MedicationReminderResponseDto
    {
        public int Id { get; set; }
        public int PatientId { get; set; }
        public string PatientName { get; set; }
        public string MedicationName { get; set; } = string.Empty;
        public TimeOnly ReminderTime { get; set; }
        public ReminderDays Days { get; set; }
        public DateOnly StartDate { get; set; }
        public DateOnly EndDate { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
