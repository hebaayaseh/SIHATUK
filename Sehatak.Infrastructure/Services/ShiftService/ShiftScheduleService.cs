using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.DTOs.ShiftDto;
using Sehatak.Application.Interfaces.IShiftSchedule;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Infrastructure.Services.ShiftService
{
    public class ShiftScheduleService : IShiftSchedule
    {
        private readonly SharedDbContext sharedDbContext;
        private readonly TenantDbContextFactory contextFactory;
        public ShiftScheduleService(SharedDbContext sharedDbContext, TenantDbContextFactory contextFactory)
        {
            this.sharedDbContext = sharedDbContext;
            this.contextFactory = contextFactory;
        }

        async Task<ShiftScheduleResponse> IShiftSchedule.AddShiftSchedule(int userId,int centerId, ShiftScheduleRequest request)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var admin = await db.Users
                .FirstOrDefaultAsync(a => a.Id == userId
                                     && a.isActive);

            if (admin == null)
                throw new BusinessException("Auth.Forbidden");

            var shiftScedule = new ShiftSchedule
            { 
                ShiftName = request.ShiftName,
                StartTime = request.StartTime,
                EndTime = request.EndTime,
            };

            await db.AddAsync(shiftScedule);
            await db.SaveChangesAsync();
            return new ShiftScheduleResponse
            {
                Id = shiftScedule.Id,
                ShiftName = request.ShiftName,
                StartTime = request.StartTime,
                EndTime = request.EndTime,
            };

        }
    }
}
