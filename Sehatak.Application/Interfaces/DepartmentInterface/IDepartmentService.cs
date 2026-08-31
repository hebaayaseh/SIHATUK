using Sehatak.Application.DTOs.DepartmentDto;
using Sehatak.Application.DTOs.StaffSignup;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.Interfaces.DepartmentInterface
{
    public interface IDepartmentService
    {
        Task<DepartmentResponseDto> AddDepartmentAsync(int centerId,DepartmentRequestDto request);
        Task<DepartmentResponseDto> UpdateDepartmentAsync(int centerId,DepartmentUpdateRequestDto request);
        Task <string> RemoveDepartmentAsync(int centerId, DepartmentRemoveRequestDto request);
        Task<DoctorResponseDto> RegisterDoctorAsync(int centerId, DoctorRequestDto request);
        Task<GetDepartmentResponseDto> GetDepartmentsAsync(int centerId);
    }
}
