using Sehatak.Application.DTOs.GetStaffDto;
using Sehatak.Application.DTOs.SearchDoctorDto;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.Interfaces.SearchDoctor
{
    public interface ISearchDoctor
    {
        Task<List<DoctorSummaryDto>> SearchDoctorAsync(int centerId, SearchDoctorRequest request);
    }
}
