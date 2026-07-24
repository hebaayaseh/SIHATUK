using Sehatak.Application.DTOs.AppointmentDto;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.Interfaces.ApointmentInterface
{
    public interface IAppointment
    {
        Task<AvailableDoctorSlot> GetAvailableDoctorSlot(int centerId, int doctorId,DateOnly date);
        Task<BookAppointmentRespesponse> BookAppointmentAsync(int centerId , int doctorId ,int userId , BookAppointmentRequest request);
        Task<string> DeleteDoctorSlotAsync(int centerId, int userId, DeleteDoctorSlotRequest request);

    }
}
