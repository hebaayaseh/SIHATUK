using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.DTOs.ServicePriceDto;
using Sehatak.Application.Interfaces.ServicePriceInterface;
using Sehatak.Domain.Entities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;

namespace Sehatak.Infrastructure.Services.ServicePriceService
{
    public class servicePrice : IServicePrice
    {
        private readonly SharedDbContext sharedDbContext;
        private readonly TenantDbContextFactory contextFactory;
        public servicePrice(SharedDbContext sharedDbContext , TenantDbContextFactory contextFactory)
        {
            this.sharedDbContext = sharedDbContext;
            this.contextFactory = contextFactory;
        }

        public async Task<ServicePriceResponse> AddServicePriceAsync(int userId , int centerId, ServicePriceRequest request)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var admin = await db.Users
                 .FirstOrDefaultAsync(u => u.Id == userId && u.role == userRole.Admin && u.isActive);

            if (admin == null)
                throw new BusinessException("Auth.Forbidden");

            var duplicateName = request.Items
                .GroupBy(s => s.ServiceName.Trim().ToLower())
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicateName.Any())
                throw new BusinessException("Validation.DuplicateServiceNames");

            var existingName = await db.ServicePrices
                .Where(t=>t.Type==request.Type && t.IsActive)
                .Select(t=>t.ServiceName.Trim().ToLower()).ToListAsync();

            var conflictingNames = request.Items
                .Where(s => existingName.Contains(s.ServiceName.Trim().ToLower()))
                .Select(s => s.ServiceName)
                .ToList();

            if (conflictingNames.Any())
                throw new BusinessException("Validation.DuplicateServiceNames");

            var item = request.Items
                .Select(s => new ServicePrice
                {
                    ServiceName = s.ServiceName,
                    Price = s.Price,
                    Type=request.Type,
                    CreatedAt= DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                }).ToList();

            await db.ServicePrices.AddRangeAsync(item);
            await db.SaveChangesAsync();

            return new ServicePriceResponse
            {
                Type = request.Type,
                CreatedItems = item.Select(s=>new ServicePriceResponseItem { 
                  Id = s.Id,
                  ServiceName = s.ServiceName,
                  Price = s.Price
                }).ToList(),

            };

        }

        public async Task<string> RemoveServicePrice(int userId, int centerId, int servicePriceId)
        {
            var center = sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var user = await db.Users
                .FirstOrDefaultAsync(a => a.Id == userId && a.isActive && a.role == userRole.Admin);

            if (user == null)
                throw new BusinessException("Auth.Forbidden");

            var servicePrice = await db.ServicePrices
                .FirstOrDefaultAsync(s => s.Id == servicePriceId && s.IsActive);

            if (servicePrice == null)
                throw new BusinessException("General.NotFound");

            servicePrice.IsActive = false;
            servicePrice.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return "The service price has been successfully removed.";
        }

        public async Task<UpaterServicePriceResponse> updateServicePrice(int userId, int centerId, UpdateServicePrice request)
        {
            var center = await sharedDbContext.MedicalCenters
               .FirstOrDefaultAsync(c => c.Id == centerId && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var admin = await db.Users
                 .FirstOrDefaultAsync(u => u.Id == userId && u.role == userRole.Admin && u.isActive);

            if (admin == null)
                throw new BusinessException("Auth.Forbidden");

            var updateService = await db.ServicePrices
                .FirstOrDefaultAsync(s => s.IsActive && s.Id == request.ServicePriceId);

            if (updateService == null)
                throw new BusinessException("General.NotFound");

            if (request.Price != null)
            {
                if (request.Price < 0)
                    throw new BusinessException("ServicePrice.InvalidPrice");
                updateService.Price = request.Price.Value;
            }

            if (!string.IsNullOrWhiteSpace(request.ServiceName))
                updateService.ServiceName = request.ServiceName.Trim();

            updateService.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return new UpaterServicePriceResponse 
            {
                Id = updateService.Id,
                Price = updateService.Price,
                ServiceName = updateService.ServiceName,
                Type = updateService.Type
            };

        }
    }
}
