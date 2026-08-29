using Sehatak.Application.DTOs.DashBoardDto;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.Interfaces.IDashBoard
{
    public interface IApointmentDashBoard
    {
        Task<AppointmentsSummaryDto> GetCenterAppointmentsSummaryAsync(int centerId, DateOnly? date = null);
    }
}
