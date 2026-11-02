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
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (IsDuplicateKey(ex))
            {
                throw new BusinessException("Attendance.AlreadyExsist");
            }

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

            return "تم تسجيل الغياب بنجاح.";

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

            var nowUtc = DateTime.UtcNow;
            var localTime = TimeOnly.FromDateTime(ClinicClock.FromUtc(nowUtc));

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
                CheckInTime = nowUtc,
                attendanceStatus = localTime > shiftTime.StartTime
                    ? AttendanceStatus.Late
                    : AttendanceStatus.Present
            };
            using var transaction = await db.Database.BeginTransactionAsync();
            await db.StaffAttendances.AddAsync(attendance);
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (IsDuplicateKey(ex))
            {
                throw new BusinessException("Attendance.AlreadyExsist");
            }

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
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId && c.CenterStatus == CenterStatus.Active);
            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            if (request.AttendanceDate != ClinicClock.Today)
                throw new BusinessException("Date.Invalid");

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.isActive);
            if (user == null)
                throw new BusinessException("User.NotFound");

            var nowUtc = DateTime.UtcNow;
            var localNow = ClinicClock.FromUtc(nowUtc);
            var localTime = TimeOnly.FromDateTime(localNow);
            var today = DateOnly.FromDateTime(localNow);
            var yesterday = today.AddDays(-1);

            // 1) وردية ليلية بدأت أمس وما انسجّل خروجها
            var overnight = await (
                from a in db.StaffAttendances
                join s in db.StaffShifts on a.StaffShiftId equals s.Id
                join t in db.shiftSchedules on s.ShiftName equals t.ShiftName
                where a.UserId == userId
                   && s.ShiftDate == yesterday
                   && a.CheckInTime != null
                   && a.CheckOutTime == null
                   && t.EndTime < t.StartTime
                select new { Attendance = a, Schedule = t })
                .FirstOrDefaultAsync();

            StaffAttendance attendance;
            ShiftSchedule shiftTime;
            var closingNextMorning = overnight != null;

            if (overnight != null)
            {
                attendance = overnight.Attendance;
                shiftTime = overnight.Schedule;
            }
            else
            {
                // 2) وردية اليوم
                var staffShift = await db.StaffShifts
                    .FirstOrDefaultAsync(s => s.UserId == userId && s.IsActive && s.ShiftDate == today);
                if (staffShift == null)
                    throw new BusinessException("User.NotFound");

                shiftTime = await db.shiftSchedules
                    .FirstOrDefaultAsync(s => s.ShiftName == staffShift.ShiftName)
                    ?? throw new BusinessException("Shift.NotFound");

                var existing = await db.StaffAttendances
                    .FirstOrDefaultAsync(a => a.UserId == userId && a.StaffShiftId == staffShift.Id);

                // غياب/إجازة (CheckInTime فاضي) أو ما دخل أصلاً
                if (existing == null || existing.CheckInTime == null)
                    throw new BusinessException("Attendance.NotFound");

                if (existing.CheckOutTime != null)
                    throw new BusinessException("Attendance.AlreadyExsist");

                attendance = existing;
            }

            var isOvernight = shiftTime.EndTime < shiftTime.StartTime;
            // وردية ليلية وما زلنا قبل منتصف الليل = أكيد خروج مبكر
            var leftEarly = (closingNextMorning || !isOvernight)
                ? localTime < shiftTime.EndTime
                : true;

            attendance.CheckOutTime = nowUtc;
            if (attendance.attendanceStatus == AttendanceStatus.Present && leftEarly)
                attendance.attendanceStatus = AttendanceStatus.EarlyOut;   // Late تبقى Late

            var auditEntry = auditLog.Build(
                action: "StaffAttendance",
                entityType: nameof(StaffAttendance),
                entityId: attendance.Id,
                newValue: new Dictionary<string, object>
                {
            { "UserId", attendance.UserId },
            { "StaffShiftId", attendance.StaffShiftId },
            { "AttendanceDate", attendance.AttendanceDate },
            { "CheckOutTime", attendance.CheckOutTime },
            { "AttendanceStatus", attendance.attendanceStatus }
                });
            if (auditEntry != null)
                db.AuditLogs.Add(auditEntry);

            await db.SaveChangesAsync();

            return "تم تسجيل الانصراف بنجاح.";
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
                .AnyAsync(a => a.UserId == userId 
                          && a.AttendanceDate == request.AttendanceDate);

            if (already)
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
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (IsDuplicateKey(ex))
            {
                throw new BusinessException("Attendance.AlreadyExsist");
            }

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
        private static bool IsDuplicateKey(DbUpdateException ex) =>
        ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 };
    }
    
}
