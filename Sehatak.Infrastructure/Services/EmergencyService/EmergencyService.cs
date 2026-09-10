using Sehatak.Application.Common;
using Sehatak.Application.DTOs.EmergencyDto;
using Sehatak.Application.Interfaces.IEmengency;
using Sehatak.Infrastructure.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Infrastructure.Services.EmergencyService
{
    public class EmergencyService : IEmengency
    {
        private readonly SharedDbContext sharedDbContext;
        private readonly TenantDbContextFactory contextFactory;
        public EmergencyService(SharedDbContext sharedDbContext, TenantDbContextFactory contextFactory)
        {
            this.sharedDbContext = sharedDbContext;
            this.contextFactory = contextFactory;
        }

        public Task<EmergencyResponseDto> EditPatientEmergencyAsync(int centerId, int userId, int emergencyId)
        {
            throw new NotImplementedException();
        }

        public Task<PagedResult<EmergencyResponseDto>> GetPatientEmergencyAsync(int centerId, PagedRequest request)
        {
            throw new NotImplementedException();
        }

        public Task<EmergencyResponseDto> RegisterPatientEmergencyAsync(int centerId, int userId, EmergencyRequestDto request)
        {
            throw new NotImplementedException();
        }
    }
}
