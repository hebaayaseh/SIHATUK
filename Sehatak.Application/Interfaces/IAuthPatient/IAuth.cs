using Sehatak.Application.DTOs.PatienRegisterDto;
using Sehatak.Application.DTOs.PatientLoginDto;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.Interfaces.AuthPatient
{
    public interface IAuth
    {
        Task<RegisterResponseDto> RegisterAsync(int centerId,RegisterRequestDto request);
        Task<VerifyOtpResponseDto> VerifyOtpAsync(int centerId, VerifyOtpRequestDto request);
        Task<PatientResponseDto> LoginPatientAsync(int centerId, PatientRequestDto request);
    }
}
