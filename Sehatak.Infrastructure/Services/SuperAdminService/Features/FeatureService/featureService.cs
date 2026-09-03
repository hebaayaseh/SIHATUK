using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.DTOs.FeatureCenterDto;
using Sehatak.Application.DTOs.FeatureDto;
using Sehatak.Application.Interfaces.Features;
using Sehatak.Domain.Entities.SharedEntities;
using Sehatak.Infrastructure.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Infrastructure.Services.SuperAdminService.Features.FeatureService
{
    public class featureService : IFeature
    {
        private readonly SharedDbContext sharedDbContext;
        public featureService(SharedDbContext sharedDbContext)
        {
            this.sharedDbContext = sharedDbContext;
        }

        public async Task<FeatureResponseDto> AddFeatureAsync(CreateFeatureRequestDto requestDto)
        {
            var feature = new PlatformFeature
            {
                NameOfFeature = requestDto.Name,
                Description = requestDto.Description,
            };
            await sharedDbContext.PlatformFeatures.AddAsync(feature);
            await sharedDbContext.SaveChangesAsync();

            return new FeatureResponseDto {
                Id = feature.Id,
                Name = feature.NameOfFeature,
                Description = feature.Description
            };
        }
        public async Task<bool> AddFeatureToCenterAsync(int centerId, AddFeatureToCenterRequest request)
        {
            var center = await sharedDbContext.MedicalCenters.FindAsync(centerId);
            if (center == null)
            {
                throw new BusinessException("Center.NotFound");
            }

            var featureExists = await sharedDbContext.PlatformFeatures
                .AnyAsync(f => f.Id == request.featureId);
            if (!featureExists)
                throw new BusinessException("General.NotFound");

            var alreadyAdded = await sharedDbContext.CenterFeatures
                .AnyAsync(cf => cf.CenterId == centerId && cf.FeatureId == request.featureId);

            if (alreadyAdded)
                throw new BusinessException("General.NotFound");

            var feature = new CenterFeature
            {
                CenterId = centerId,
                FeatureId = request.featureId,
                IsEnabled = true
            };

            await sharedDbContext.CenterFeatures.AddAsync(feature);
            await sharedDbContext.SaveChangesAsync();

            return true;

        }

        public async Task<bool> ActiveFeaturAsync(int centerId, ActiveFetureRequest request)
        {
            var center = await sharedDbContext.MedicalCenters.FindAsync(centerId);

            if (center == null)
                throw new BusinessException("Center.NotFound");


            var feature = await sharedDbContext.PlatformFeatures.FirstOrDefaultAsync
                (f => f.Id == request.FetureId);

            if (feature == null)
                throw new BusinessException("General.NotFound");

            var existingCenterFeature = await sharedDbContext.CenterFeatures
                .FirstOrDefaultAsync(cf => cf.CenterId == centerId
                && cf.FeatureId == request.FetureId && cf.IsEnabled == false);

            if (existingCenterFeature == null)
            {
                throw new BusinessException("General.NotFound");
            }

            existingCenterFeature.IsEnabled = true;
            await sharedDbContext.SaveChangesAsync();

            return true;

        }

        public async Task<Application.Common.PagedResult<FeatureResponseDto>> GetAllFeatureAsync(PagedRequest request)
        {
            var query = sharedDbContext.PlatformFeatures
                .Select(f => new FeatureResponseDto
                {
                    Id = f.Id,
                    Name = f.NameOfFeature,
                    Description = f.Description
                });

            return await query.ToPagedResultAsync(request.PageNumber, request.PageSize);
        }

        public async Task<bool> RemoveFeatureFromCenterAsync(int centerId, RemoveFeatureFromCenterRequest request)
        {

            var center = await sharedDbContext.MedicalCenters.FindAsync(centerId);

            if (center == null)
            {
                throw new BusinessException("Center.NotFound");
            }

            var feature = await sharedDbContext.PlatformFeatures
                .FirstOrDefaultAsync(f => f.Id == request.featureId);
            if (feature == null)
            {
                throw new BusinessException("General.NotFound");
            }

            var centerFeature = await sharedDbContext.CenterFeatures
                .FirstOrDefaultAsync(cf => cf.CenterId == centerId
            && cf.FeatureId == request.featureId);
            if (centerFeature == null)
            {
                throw new BusinessException("General.NotFound");
            }

            centerFeature.IsEnabled = false;

            await sharedDbContext.SaveChangesAsync();

            return true;

        }

    }
}
