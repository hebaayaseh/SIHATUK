using Sehatak.Application.DTOs.ConsultationDto;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.Interfaces.ConsultaionInterface
{
    public interface IConsultation
    {
        Task<List<DoctorEnableResponse>> GetDoctorEnableConsultation(int centerId);

    }
}
