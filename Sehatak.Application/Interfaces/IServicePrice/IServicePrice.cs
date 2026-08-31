using Sehatak.Application.DTOs.ServicePriceDto;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.Interfaces.ServicePriceInterface
{
    public interface IServicePrice
    {
        Task<ServicePriceResponse> AddServicePriceAsync(int userId , int centerId, ServicePriceRequest request);
        Task<UpaterServicePriceResponse> updateServicePrice(int userId, int centerId, UpdateServicePrice request);
        Task<string> RemoveServicePrice(int userId , int centerId , int servicePriceId);
    }
}
