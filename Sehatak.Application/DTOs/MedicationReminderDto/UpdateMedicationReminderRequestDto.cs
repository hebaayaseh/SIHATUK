

namespace Sehatak.Application.DTOs.MedicationReminderDto
{
    public class UpdateMedicationReminderRequestDto
    {
        public string? MedicationName { get; set; } = string.Empty;
        public TimeOnly? ReminderTime { get; set; }
        public bool? IsDaily { get; set; }
        public ReminderDays? SelectedDays { get; set; }
        public int? DurationInDays { get; set; }
        public string? Notes { get; set; }
    }
}
