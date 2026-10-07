
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.MedicationReminderDto;

namespace Sehatak.Application.Interfaces.IMedicationReminder
{
    public interface IMedicationReminder
    {
        Task<MedicationReminderResponseDto> AddMedicationReminderAsync(int centerId, int userId, MedicationReminderRequestDto request,int? subPatientId);
        Task<PagedResult<MedicationReminderResponseDto>> GetMedicationRemindersAsync(int centerId, int userId, PagedRequest request, int? subPatientId);
        Task<string> DeleteMedicationReminderAsync(int centerId, int userId, int reminderId, int? subPatientId);
    }
}
