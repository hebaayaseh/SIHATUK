using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.DashBoardDto;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.Interfaces.ApointmentInterface;
using Sehatak.Application.Interfaces.IDashBoard;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;

namespace Sehatak.Infrastructure.Services.DashBoardService
{
    public class AppointmentDashBoard : IApointmentDashBoard
    {
        private readonly SharedDbContext sharedDbContext;
        private readonly TenantDbContextFactory contextFactory;
        public AppointmentDashBoard(SharedDbContext sharedDbContext, TenantDbContextFactory contextFactory)
        {
            this.sharedDbContext = sharedDbContext;
            this.contextFactory = contextFactory;
        }

        public async Task<AppointmentsSummaryDto> GetCenterAppointmentsSummaryAsync(int centerId, DateOnly? date = null)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId 
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            var targetDate = date ?? DateOnly.FromDateTime(DateTime.UtcNow);

            using var db = contextFactory.CreateForCenter(centerId);

            var appointments = await db.Appointments
                .Where(a => a.appointmentDate == targetDate)
                .Select(a => a.appointmentStatus)
                .ToListAsync();

            var waitlistCount = await db.Waitlists
                .CountAsync(w => w.PreferredDate == targetDate 
                            && w.Status == WaitlistStatus.Waiting);

            return new AppointmentsSummaryDto
            {
                Date = targetDate,
                Total = appointments.Count,
                Confirmed = appointments.Count(s => s == AppointmentStatus.Confirmed),
                Completed = appointments.Count(s => s == AppointmentStatus.Completed),
                Cancelled = appointments.Count(s => s == AppointmentStatus.Cancelled),
                NoShow = appointments.Count(s => s == AppointmentStatus.NoShow),
                WaitlistCount = waitlistCount
            };
        }
    }
}
