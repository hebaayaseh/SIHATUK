using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.DTOs.StaffAttendance;
using Sehatak.Application.Interfaces.IStaffAttendance;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Infrastructure.Services.StaffAttendanceService
{
    public class StaffAttendanceService : IStaffAttendance
    {
        private readonly SharedDbContext sharedDbContext;
        private readonly TenantDbContextFactory contextFactory;
        public StaffAttendanceService(SharedDbContext sharedDbContext , TenantDbContextFactory contextFactory)
        {
            this.sharedDbContext = sharedDbContext;
            this.contextFactory = contextFactory;
        }
        public async Task<string> CheckInTimeAsync(int centerId, int userId, StaffAttendanceCheckInRequestDto request)
        {
            var center = await sharedDbContext
                .MedicalCenters.FirstOrDefaultAsync(c=>c.Id == centerId
                                                    && c.CenterStatus == CenterStatus.Active);
            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);
            if (request.AttendanceDate != DateOnly.FromDateTime(DateTime.UtcNow))
                throw new BusinessException("Invalid.Date");

            var user = await db.Users
                .FirstOrDefaultAsync(u => u.Id == userId
                                     && u.isActive);

            if (user == null)
                throw new BusinessException("User.NotFound");

            var staffShift = await db.StaffShifts
                .FirstOrDefaultAsync(s => s.UserId == userId
                                     && s.IsActive
                                     && s.ShiftDate == request.AttendanceDate);

            if (staffShift == null)
                throw new BusinessException("User.NotFound");

            var shiftTime = await db.shiftSchedules
                .FirstOrDefaultAsync(s => s.ShiftName == staffShift.ShiftName);
            if (shiftTime == null)
                throw new BusinessException("Shift.NotFound");

            var already = await db.StaffAttendances
                .FirstOrDefaultAsync(a=>a.UserId==userId
                                     && a.CheckInTime!=null);
            if (already != null)
                throw new BusinessException("Attendance.AlreadyExsist");

            var attendance = new StaffAttendance
            {
                UserId = userId,
                StaffShiftId = staffShift.Id,
                AttendanceDate = request.AttendanceDate,
                CheckInTime = request.CheckTime,
            };


            var checkInTimeOnly = TimeOnly.FromDateTime(request.CheckTime);
            if (checkInTimeOnly > shiftTime.StartTime)
            {
                attendance.attendanceStatus = AttendanceStatus.Late;
            }

            attendance.attendanceStatus = AttendanceStatus.Present;
            await db.StaffAttendances.AddAsync(attendance);
            await db.SaveChangesAsync();
            return "تم تسجيل الحضور بنجاح.";
        }

        public async Task<string> CheckOutTimeAsync(int centerId, int userId, StaffAttendanceCheckInRequestDto request)
        {
            var center = await sharedDbContext
                    .MedicalCenters.FirstOrDefaultAsync(c => c.Id == centerId
                                                        && c.CenterStatus == CenterStatus.Active);
            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            if (request.AttendanceDate != DateOnly.FromDateTime(DateTime.UtcNow))
                throw new BusinessException("Invalid.Date");

            var user = await db.Users
                .FirstOrDefaultAsync(u => u.Id == userId
                                     && u.isActive);

            if (user == null)
                throw new BusinessException("User.NotFound");

            var staffShift = await db.StaffShifts
                .FirstOrDefaultAsync(s => s.UserId == userId
                                     && s.IsActive
                                     && s.ShiftDate == request.AttendanceDate);

            if (staffShift == null)
                throw new BusinessException("User.NotFound");

            var shiftTime = await db.shiftSchedules
                .FirstOrDefaultAsync(s => s.ShiftName == staffShift.ShiftName);
            if (shiftTime == null)
                throw new BusinessException("Shift.NotFound");

            var alreadyExsist = await db.StaffAttendances
                .FirstOrDefaultAsync(a => a.UserId == userId
                                     && a.CheckOutTime != null);
            if (alreadyExsist != null)
                throw new BusinessException("Attendance.AlreadyExsist");

            var already = await db.StaffAttendances
                .FirstOrDefaultAsync(a => a.UserId == userId);
            if (already == null)
                throw new BusinessException("Attendance.NotFound");
            already.CheckOutTime = request.CheckTime;
            

            var checkOutTimeOnly = TimeOnly.FromDateTime(request.CheckTime);
            if (checkOutTimeOnly < shiftTime.EndTime)
            {
                already.attendanceStatus = AttendanceStatus.EarlyOut;
            }

            already.attendanceStatus = AttendanceStatus.Present;
            await db.SaveChangesAsync();
            return "تم تسجيل الحضور بنجاح.";
        }

        public async Task<string> OnLeaveAsync(int centerId, int userId, StaffOnLeaveRequestDto request)
        {
            var center = await sharedDbContext
                .MedicalCenters.FirstOrDefaultAsync(c => c.Id == centerId
                                        && c.CenterStatus == CenterStatus.Active);
            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);
            if (request.AttendanceDate != DateOnly.FromDateTime(DateTime.UtcNow))
                throw new BusinessException("Invalid.Date");

            var user = await db.Users
                .FirstOrDefaultAsync(u => u.Id == userId
                                     && u.isActive);

            if (user == null)
                throw new BusinessException("User.NotFound");

            var staffShift = await db.StaffShifts
                .FirstOrDefaultAsync(s => s.UserId == userId
                                     && s.IsActive
                                     && s.ShiftDate == request.AttendanceDate);

            if (staffShift == null)
                throw new BusinessException("User.NotFound");

            var shiftTime = await db.shiftSchedules
                .FirstOrDefaultAsync(s => s.ShiftName == staffShift.ShiftName);
            if (shiftTime == null)
                throw new BusinessException("Shift.NotFound");

            var already = await db.StaffAttendances
                .FirstOrDefaultAsync(a => a.UserId == userId
                                     && a.attendanceStatus==AttendanceStatus.OnLeave
                                     && a.StaffShiftId == staffShift.Id);
            if (already != null)
                throw new BusinessException("Attendance.AlreadyExsist");

            var attendance = new StaffAttendance
            {
                UserId = userId,
                StaffShiftId = staffShift.Id,
                AttendanceDate = request.AttendanceDate,
                CheckInTime = null,
                CheckOutTime = null
            };

            attendance.attendanceStatus = AttendanceStatus.OnLeave;
            await db.StaffAttendances.AddAsync(attendance);
            await db.SaveChangesAsync();
            return "تم تسجيل الاجازة بنجاح.";
        }
    }
    
}
