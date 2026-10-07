using Sehatak.Domain.Entities.TenantEntities;

public class MedicationReminder
{
    public int Id { get; set; }
    public int PatientId { get; set; }
    public string MedicationName { get; set; } = string.Empty;
    public TimeOnly ReminderTime { get; set; }
    public ReminderDays Days { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }          
    public bool IsActive { get; set; } = true;
    public DateOnly? LastNotifiedDate { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation Proparity : 
    public Patient Patient { get; set; } = null!;
}