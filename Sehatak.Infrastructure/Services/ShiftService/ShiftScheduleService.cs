using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.DTOs.ShiftDto;
using Sehatak.Application.Interfaces.IShiftSchedule;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Infrastructure.Services.ShiftService
{
    public class ShiftScheduleService : IShift
    {
        private readonly SharedDbContext sharedDbContext;
        private readonly TenantDbContextFactory contextFactory;
        public ShiftScheduleService(SharedDbContext sharedDbContext, TenantDbContextFactory contextFactory)
        {
            this.sharedDbContext = sharedDbContext;
            this.contextFactory = contextFactory;
        }

        public async Task<ShiftScheduleResponse> AddShiftSchedule(int userId,int centerId, ShiftScheduleRequest request)
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

        public async Task<string> AssignShiftToStaffAsync(int userId, int centerId, AssignShiftToStaffRequestDto request)
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

            var user = await db.Users
                .FirstOrDefaultAsync(u => u.Id == request.UserId
                                     && (u.role == userRole.GeneralDoctor
                                     || u.role == userRole.Nurse
                                     || u.role == userRole.LabTechnician)
                                     && u.isActive);

            if (user == null)
                throw new BusinessException("Staff.NotFound");

            var StaffShift = new StaffShift
            {
                IsActive = true,
                UserId = request.UserId,
                ShiftDate = request.ShiftDate,
                ShiftName = request.ShiftName

            };
            await db.AddAsync(StaffShift);
            await db.SaveChangesAsync();

            return "تمت العملية بنجاح.";
        }
    }
}
