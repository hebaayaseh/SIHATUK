

using Sehatak.Application.Common;
using Sehatak.Application.DTOs.LabDto;

namespace Sehatak.Application.Interfaces.ILab
{
    public interface ILab
    {
        Task<LabRequestResponseDto> CreateLabRequestAsync(int centerId,int userId, CreateLabRequestDto request);
        Task<LabRequestResponseDto> UpdateLabRequestAsync(int centerId, int userId, UpdateLabRequestDto request);
        Task<PagedResult<LabRequestResponseDto>> GetLabRequestForPatientAsync(int centerId, int userId, int patientId,PagedRequest request);
        Task<PagedResult<PatientGetLabRequestReponseDto>> PatientGetLabRequestAsync(int centerId, int userId , PagedRequest request, int?subPatientId);
        Task<PagedResult<PatientGetLabResultReponseDto>> PatientGetLabResultAsync(int centerId, int userId, PagedRequest request, int? subPatientId);
        Task<ReceptionistLabRequestReponseDto> ReceptionistCreateLabRequestAsync(int centerId, int userId, ReceptionistCreateLabRequestDto request);
        Task<ReceptionistLabRequestReponseDto> ReceptionistUpdateLabRequestAsync(int centerId , int userId ,  ReceptionistUpdateLabRequestDto request);
        Task<string> CancleLabRequestAsync(int centerId,int userId , int labRequestId);
        Task<PagedResult<LabGetRequestResponseDto>> LabGetPendingRequestsAsync(int centerId, int userId , PagedRequest request);
        Task<LabGetRequestResponseDto> labGetRequestAsync(int centerId,int userId, int labRequestId);
        Task<string> LabCollectSample(int centerId,int userId, int labRequestId,List<int>? unavailableItemIds);
        Task<LabUploadResultResponseDto> LabUploadResult(int centerId , int userId , UploadLabResultRequestDto request);
        Task<GetLabResultDto> DoctorGetLabResultsForPatient(int centerId, int userId, int patientId,int labRequestId);
        Task<PagedResult<LabGetRequestResponseDto>> ReceptionistGetLabRequestsAwaitingPaymentAsync(int centerId, int userId, PagedRequest request);
    }
}
