using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.DTOs.ShiftDto;
using Sehatak.Application.Interfaces.IShiftSchedule;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;

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

        public async Task<ShiftScheduleResponse> AddShiftSchedule(int centerId, ShiftScheduleRequest request)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var exists = await db.shiftSchedules
                 .AnyAsync(s => s.ShiftName == request.ShiftName);

            if (exists)
                throw new BusinessException("ShiftSchedule.AlreadyExists");

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

        public async Task<string> AssignShiftToStaffAsync(int centerId, AssignShiftToStaffRequestDto request)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);


            var scheduleExists = await db.shiftSchedules
                .FirstOrDefaultAsync(s => s.ShiftName == request.ShiftName);
            if (scheduleExists == null)
                throw new BusinessException("ShiftSchedule.NotConfigured");

            var user = await db.Users
                .FirstOrDefaultAsync(u => u.Id == request.UserId
                                     && (u.role == userRole.GeneralDoctor
                                     || u.role == userRole.Nurse
                                     || u.role == userRole.LabTechnician)
                                     && u.isActive);

            if (user == null)
                throw new BusinessException("Staff.NotFound");

            
            var alreadyAssigned = await db.StaffShifts
               .AnyAsync(s => s.UserId == request.UserId &&
                         s.ShiftDate == request.ShiftDate 
                         && s.ShiftName == request.ShiftName
                         && s.IsActive);

            if (alreadyAssigned)
                throw new BusinessException("StaffShift.AlreadyAssigned");

            var StaffShift = new StaffShift
            {
                IsActive = true,
                UserId = request.UserId,
                ShiftDate = request.ShiftDate,
                ShiftName = request.ShiftName

            };
            await db.AddAsync(StaffShift);

            await db.Notifications.AddAsync(new Notification
            {
                UserId = request.UserId,
                CreatedAt = DateTime.UtcNow,
                IsRead = false,
                Type = NotificationType.Shift,
                Message = $"دوامك لليوم : \n {request.ShiftDate} " +
                $"\n{request.ShiftName.ToString()} " +
                $"{scheduleExists.StartTime} - {scheduleExists.EndTime}" 
            });
            await db.SaveChangesAsync();

            return "تمت العملية بنجاح.";
        }

        public async Task<string> DeleteShiftSchedualeAsync(int centerId, int ShiftId)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var shift = await db.shiftSchedules
                .FirstOrDefaultAsync(s => s.Id == ShiftId);

            if (shift == null)
                throw new BusinessException("Shift.NotFound");

            var staffs = await db.StaffShifts
                .Where(s => s.ShiftName == shift.ShiftName
                      && s.IsActive
                      && s.ShiftDate >= DateOnly.FromDateTime(DateTime.UtcNow))
                .ToListAsync();

            db.shiftSchedules.Remove(shift);

            if (staffs.Any())
            {
                foreach (var staff in staffs)
                {
                    await db.Notifications.AddRangeAsync(new Notification
                    {
                        UserId = staff.UserId,
                        Type = NotificationType.Shift,
                        Message = $"تم حذف جدول الدوام انتظروا التعديل ",
                        IsRead = false
                    });
                    staff.IsActive = false;
                }
            }
            await db.SaveChangesAsync();
            return "تم الحذف بنجاح.";
        }

        public async Task<GetShiftsScheduleResponseDto> GetShiftsSchedulesAsync(int centerId)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var shifts = await db.shiftSchedules
                .Select(s => new ShiftScheduleResponse
                {
                    Id = s.Id,
                    ShiftName = s.ShiftName,
                    StartTime = s.StartTime,
                    EndTime = s.EndTime
                }).ToListAsync();

            return new GetShiftsScheduleResponseDto
            {
                ShiftsSchedule = shifts
            };
        }

        public async Task<List<GetStaffsShitfResponseDto>> GetStaffsAsync(int centerId, ShiftGroup shift, int? year = null, int? month = null)
        {
            var center = await sharedDbContext.MedicalCenters
               .FirstOrDefaultAsync(c => c.Id == centerId
                                    && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            var targetYear = year ?? DateTime.UtcNow.Year;
            var targetMonth = month ?? DateTime.UtcNow.Month;

            var startOfMonth = new DateOnly(targetYear, targetMonth, 1);
            var endOfMonth = startOfMonth.AddMonths(1).AddDays(-1);

            using var db = contextFactory.CreateForCenter(centerId);

            var shifts = await db.StaffShifts
                .Include(s => s.Staff)
                .Where(s => s.ShiftName == shift
                       && s.ShiftDate >= startOfMonth
                       && s.ShiftDate <= endOfMonth)
                .ToListAsync();

            var shiftIds = shifts.Select(i => i.Id).ToList();

            var attendances = await db.StaffAttendances
                .Where(a=> shiftIds.Contains(a.StaffShiftId))
                .ToListAsync();

            var result = shifts
                .GroupBy(u => u.UserId)
                .Select(g =>
                {
                    var staff = g.First().Staff;
                    return new GetStaffsShitfResponseDto
                    {
                        userId = staff.Id,
                        name = $"{staff.firstName} {staff.lastName}",
                        email = staff.email,
                        phoneNumber = staff.phoneNumber,
                        role = staff.role.ToString(),
                        userIsActive = staff.isActive,
                        Days = g.Select(s => new DailyAttendanceDto
                        {
                            Date = s.ShiftDate,
                            Status = attendances.FirstOrDefault(a => a.StaffShiftId == s.Id)?.attendanceStatus,
                            isActive = s.IsActive,
                        })
                        .OrderBy(d => d.Date)
                        .ToList()
                    };
                }).ToList();

            return result;
        }

        public async Task<ShiftScheduleResponse> UpdateShiftScheduleAsync(int centerId, UpdateShiftSchedualRequestDto request)
        {
            var center = await sharedDbContext.MedicalCenters
               .FirstOrDefaultAsync(c => c.Id == centerId
                                    && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var shift = await db.shiftSchedules
                .FirstOrDefaultAsync(s => s.Id == request.ShiftId);

            if (shift == null)
                throw new BusinessException("Shift.NotFound");

            var staffs = await db.StaffShifts
                .Where(s => s.ShiftName == shift.ShiftName
                       && s.IsActive
                       && s.ShiftDate>= DateOnly.FromDateTime(DateTime.UtcNow))
                .ToListAsync();


            if (request.StartTime != null)
                shift.StartTime = (TimeOnly)request.StartTime;

            if (request.EndTime != null)
                shift.EndTime = (TimeOnly)request.EndTime;

            if (request.ShiftName != null)
                shift.ShiftName = (ShiftGroup)request.ShiftName;

            if (staffs.Any())
            {
                foreach (var staff in staffs)
                {
                    await db.Notifications.AddRangeAsync(new Notification
                    {
                        UserId = staff.UserId,
                        Type = NotificationType.Shift,
                        Message =$"تم تعديل موعد الدوام الى " +
                        $"\n{shift.StartTime} - {shift.EndTime}",
                        CreatedAt = DateTime.UtcNow,
                        IsRead = false
                    });
                }
            }

            await db.SaveChangesAsync();
            return new ShiftScheduleResponse
            {
                Id = shift.Id,
                ShiftName = shift.ShiftName,
                StartTime = shift.StartTime,
                EndTime = shift.EndTime
            };
        }
    }
}
