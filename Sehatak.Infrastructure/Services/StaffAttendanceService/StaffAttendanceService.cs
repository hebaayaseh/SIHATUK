using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.DTOs.StaffAttendance;
using Sehatak.Application.Interfaces.IStaffAttendance;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;
using Sehatak.Application.Common;
using Sehatak.Application.Interfaces.AuditLog;

namespace Sehatak.Infrastructure.Services.StaffAttendanceService
{
    public class StaffAttendanceService : IStaffAttendance
    {
        private readonly SharedDbContext sharedDbContext;
        private readonly TenantDbContextFactory contextFactory;
        private readonly IAuditLog auditLog;
        public StaffAttendanceService(SharedDbContext sharedDbContext , TenantDbContextFactory contextFactory, IAuditLog auditLog)
        {
            this.sharedDbContext = sharedDbContext;
            this.contextFactory = contextFactory;
            this.auditLog = auditLog;
        }

        public async Task<string> AbsentStaffAsync(int centerId, StaffAbsentRequestDto request)
        {
            var center = await sharedDbContext
               .MedicalCenters.FirstOrDefaultAsync(c => c.Id == centerId
                                                   && c.CenterStatus == CenterStatus.Active);
            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            if (request.AttendanceDate != ClinicClock.Today)
                throw new BusinessException("Date.Invalid");

            var user = await db.Users
                .FirstOrDefaultAsync(u => u.Id == request.userId
                                     && u.isActive);

            if (user == null)
                throw new BusinessException("User.NotFound");

            var staffShift = await db.StaffShifts
                .FirstOrDefaultAsync(s => s.UserId == request.userId
                                     && s.IsActive
                                     && s.ShiftDate == request.AttendanceDate);

            if (staffShift == null)
                throw new BusinessException("User.NotFound");

            var shiftTime = await db.shiftSchedules
                .FirstOrDefaultAsync(s => s.ShiftName == staffShift.ShiftName);
            if (shiftTime == null)
                throw new BusinessException("Shift.NotFound");

            
            var already = await db.StaffAttendances
                .FirstOrDefaultAsync(a => a.UserId == request.userId
                                     && a.StaffShiftId == staffShift.Id);

            if (already != null)
                throw new BusinessException("Attendance.AlreadyExsist");

            var attendance = new StaffAttendance
            {
                UserId = request.userId,
                StaffShiftId = staffShift.Id,
                AttendanceDate = request.AttendanceDate
            };

            attendance.attendanceStatus = AttendanceStatus.Absent;
            using var transaction = await db.Database.BeginTransactionAsync();
            await db.StaffAttendances.AddAsync(attendance);
            await db.SaveChangesAsync();

            var auditEntry = auditLog.Build(
                action : "StaffAttendance",
                entityType : nameof(StaffAttendance),
                entityId : attendance.Id,
                newValue : new Dictionary<string, object>
                {
                    { "UserId", attendance.UserId },
                    { "StaffShiftId", attendance.StaffShiftId },
                    { "AttendanceDate", attendance.AttendanceDate },
                    { "AttendanceStatus", attendance.attendanceStatus }
                });

            if (auditEntry != null)
                db.AuditLogs.Add(auditEntry);

            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            return "تم تسجيل الاجازة بنجاح.";

        }

        public async Task<string> CheckInTimeAsync(int centerId, int userId, StaffAttendanceCheckInRequestDto request)
        {
            var center = await sharedDbContext
                .MedicalCenters.FirstOrDefaultAsync(c=>c.Id == centerId
                                                    && c.CenterStatus == CenterStatus.Active);
            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);
            if (request.AttendanceDate != ClinicClock.Today)
                throw new BusinessException("Date.Invalid");

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
                                     && a.StaffShiftId == staffShift.Id);
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
            attendance.attendanceStatus = checkInTimeOnly > shiftTime.StartTime
                ? AttendanceStatus.Late
                : AttendanceStatus.Present;
            using var transaction = await db.Database.BeginTransactionAsync();
            await db.StaffAttendances.AddAsync(attendance);
            await db.SaveChangesAsync();

            var auditEntry = auditLog.Build(
                action: "StaffAttendance",
                entityType: nameof(StaffAttendance),
                entityId: attendance.Id,
                newValue: new Dictionary<string, object>
                {
                    { "UserId", attendance.UserId },
                    { "StaffShiftId", attendance.StaffShiftId },
                    { "AttendanceDate", attendance.AttendanceDate },
                    { "CheckInTime", attendance.CheckInTime },
                    { "AttendanceStatus", attendance.attendanceStatus }
                });

            if (auditEntry != null)
                db.AuditLogs.Add(auditEntry);

            await db.SaveChangesAsync();
            await transaction.CommitAsync();

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

            if (request.AttendanceDate != ClinicClock.Today)
                throw new BusinessException("Date.Invalid");

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
                                     && a.StaffShiftId == staffShift.Id
                                     && a.CheckOutTime != null);
            if (alreadyExsist != null)
                throw new BusinessException("Attendance.AlreadyExsist");

            
            var already = await db.StaffAttendances
                .FirstOrDefaultAsync(a => a.UserId == userId
                                     && a.StaffShiftId == staffShift.Id);
            if (already == null)
                throw new BusinessException("Attendance.NotFound");
            already.CheckOutTime = request.CheckTime;

            var checkOutTimeOnly = TimeOnly.FromDateTime(request.CheckTime);
            already.attendanceStatus = checkOutTimeOnly < shiftTime.EndTime
                ? AttendanceStatus.EarlyOut
                : AttendanceStatus.Present;

            var auditEntry = auditLog.Build(
                action: "StaffAttendance",
                entityType: nameof(StaffAttendance),
                entityId: already.Id,
                newValue: new Dictionary<string, object>
                {
                    { "UserId", already.UserId },
                    { "StaffShiftId", already.StaffShiftId },
                    { "AttendanceDate", already.AttendanceDate },
                    { "CheckOutTime", already.CheckOutTime },
                    { "AttendanceStatus", already.attendanceStatus }
                });
            if (auditEntry != null)
                db.AuditLogs.Add(auditEntry);

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

            if (request.AttendanceDate != ClinicClock.Today)
                throw new BusinessException("Date.Invalid");

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
            using var transaction = await db.Database.BeginTransactionAsync();

            await db.StaffAttendances.AddAsync(attendance);
            await db.SaveChangesAsync();

            var auditEntry = auditLog.Build(
                action: "StaffAttendance",
                entityType: nameof(StaffAttendance),
                entityId: attendance.Id,
                newValue: new Dictionary<string, object>
                {
                    { "UserId", attendance.UserId },
                    { "StaffShiftId", attendance.StaffShiftId },
                    { "AttendanceDate", attendance.AttendanceDate },
                    { "AttendanceStatus", attendance.attendanceStatus }
                });
            if (auditEntry != null)
                db.AuditLogs.Add(auditEntry);

            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            return "تم تسجيل الاجازة بنجاح.";
        }
    }
    
}
