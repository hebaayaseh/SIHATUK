using Sehatak.Application.DTOs.PatienRegisterDto;
using Sehatak.Application.DTOs.PatientLoginDto;

namespace Sehatak.Application.Interfaces.AuthPatient
{
    public interface IAuth
    {
        Task<RegisterResponseDto> RegisterAsync(int centerId,RegisterRequestDto request);
        Task<VerifyOtpResponseDto> VerifyOtpAsync(int centerId, VerifyOtpRequestDto request);
        Task<PatientResponseDto> LoginPatientAsync(int centerId, PatientRequestDto request);
        Task<string> PatientDeactiveProfileAsync(int centerId, int userId);
        Task<PatientResponseDto> PatientActiveProfileAsync(int centerId, PatientRequestDto request);

    }
}
