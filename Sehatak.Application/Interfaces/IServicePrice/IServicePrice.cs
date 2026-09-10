using Sehatak.Application.Common;
using Sehatak.Application.DTOs.ServicePriceDto;
using Sehatak.Domain.Enums;

namespace Sehatak.Application.Interfaces.ServicePriceInterface
{
    public interface IServicePrice
    {
        Task<ServicePriceResponse> AddServicePriceAsync(int userId , int centerId, ServicePriceRequest request);
        Task<UpaterServicePriceResponse> updateServicePrice(int userId, int centerId, UpdateServicePrice request);
        Task<string> RemoveServicePrice(int userId , int centerId , int servicePriceId);
        Task<PagedResult<GetServicePriceResponseDto>> GetServicePriceByCenterId(int centerId,ServiceType type,PagedRequest request);
    }
}
