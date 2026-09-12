

using Sehatak.Application.DTOs.LabDto;

namespace Sehatak.Application.Interfaces.ILab
{
    public interface ILab
    {
        Task<LabRequestResponseDto> CreateLabRequestAsync(int centerId,int userId, CreateLabRequestDto request);
        Task<LabRequestResponseDto> UpdateLabRequestAsync(int centerId, int userId, UpdateLabRequestDto request);
    }
}
