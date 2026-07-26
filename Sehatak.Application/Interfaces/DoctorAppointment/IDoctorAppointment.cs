using Sehatak.Application.DTOs.DoctorDto;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.Interfaces.DoctorAppointment
{
    public interface IDoctorAppointment
    {
        Task<DoctorAppointmentResponse> GetDoctorAppointmentsForDayAsync(int centerId, int userId , DateOnly? date);
    }
}
