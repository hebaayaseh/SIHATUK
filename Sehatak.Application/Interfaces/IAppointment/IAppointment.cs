using Sehatak.Application.Common;
using Sehatak.Application.DTOs.AppointmentDto;
using Sehatak.Application.DTOs.GetStaffDto;

namespace Sehatak.Application.Interfaces.ApointmentInterface
{
    public interface IAppointment
    {
        Task<GetDoctorSummaryResponse> GetDoctorAsync(int centerId, int doctorId);
        Task<AvailableDoctorSlot> GetAvailableDoctorSlot(int centerId, int doctorId,DateOnly date);
        Task<BookAppointmentRespesponse> BookAppointmentAsync(int centerId , int doctorId ,int userId , BookAppointmentRequest request);
        Task<string> DeleteDoctorSlotAsync(int centerId, int userId, DeleteDoctorSlotRequest request);
        Task<string> CancelAppointmentAsync(int centerId , int doctor ,int userId , CancelAppointmentRequest request);
        Task<BookAppointmentRespesponse> RescheduleAppointmentAsync(int centerId , int doctorId , int userId , RescheduleAppointmentRequest request);
        Task<string> JoinWaitListAsync(int centerId, int doctorId, int userId, DateOnly date,int?subPatientId);
        Task<PagedResult<GetPatientWaitList>> GetPatientsWaitListsAsync(int centerId,int doctorId,DateOnly date,PagedRequest request);
        Task<GetPatientWaitList> GetPatientWaitListsAsync(int centerId, int doctorId, int userId ,DateOnly date,int?subPatientId);
        Task<PagedResult<GetDoctorsResponseDto>> GetDoctorsAsync(int centerId, PagedRequest request);
        Task<BookAppointmentRespesponse> ReceptionistBookAppointmentAsync(int centerId,int userId, int doctorId, ReceptionistBookRequestDto request);
        Task<string> ReceptionistCancelAppointmentAsync(int centerId, int doctorId, int userId, ReceptionistCancelAppointmentRequest request);
        Task<BookAppointmentRespesponse> ReceptionistRescheduleAppointmentAsync(int centerId, int doctorId, int userId, ReceptionistRescheduleAppointmentRequest request);
        Task<ReceptionistEnterFromWaitlistResponse> ReceptionistEnterFromWaitlistAsync(int centerId, int doctorId, int userId, ReceptionistEnterFromWaitlistRequest request);
        Task<string> ReceptionistRemoveFromWaitlistAsync(int centerId, int doctorId, int userId, int waitListId);

    }
}
