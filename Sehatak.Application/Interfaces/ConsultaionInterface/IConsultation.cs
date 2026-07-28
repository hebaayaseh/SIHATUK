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
        Task<string> ConsultationRequest(int centerId, int doctorId, int userId);
        Task<List<ConsultationResponse>> ViewConsultations(int centerId ,  int userId);
        Task<ConsultationResponse> ViewConsultation(int centerId, int doctorId, int userId);
        Task<string> ConsultationPayment(int centerId, int consultationId, int userId);

    }
}
