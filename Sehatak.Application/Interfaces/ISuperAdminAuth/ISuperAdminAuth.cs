using Sehatak.Application.DTOs.SuperAdminDto;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.Interfaces.SuperAdminInterface
{
    public interface ISuperAdminAuth
    {
        Task<RegisterSuperAdminResponseDto> RegisterAsync(RegisterSuperAdminRequestDto request);
        Task<SuperAdminLoginResponseDto?> LoginAsync(SuperAdminLoginRequestDto request);
    }
}
