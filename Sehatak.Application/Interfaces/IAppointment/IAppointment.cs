using Sehatak.Application.Common;
using Sehatak.Application.DTOs.AppointmentDto;
using Sehatak.Application.DTOs.GetStaffDto;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        Task<string> JoinWaitListAsync(int centerId, int doctorId, int userId, DateOnly date);
        Task<PagedResult<GetPatientWaitList>> GetPatientsWaitListsAsync(int centerId,int doctorId,DateOnly date,PagedRequest request);
        Task<GetPatientWaitList> GetPatientWaitListsAsync(int centerId, int doctorId, int userId ,DateOnly date);
        Task<PagedResult<GetDoctorsResponseDto>> GetDoctorsAsync(int centerId, PagedRequest request);
    }
}
