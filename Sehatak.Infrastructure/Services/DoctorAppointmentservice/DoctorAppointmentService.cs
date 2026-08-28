using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.DoctorDto;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.Interfaces.DoctorAppointment;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Infrastructure.Services.DoctorAppointmentservice
{
    public class DoctorAppointmentService : IDoctorAppointment
    {
        private readonly SharedDbContext sharedDbContext;
        private readonly TenantDbContextFactory contextFactory;
        public DoctorAppointmentService(SharedDbContext sharedDbContext , TenantDbContextFactory contextFactory)
        {
            this.sharedDbContext = sharedDbContext;
            this.contextFactory = contextFactory;
        }

        public async Task<DoctorAppointmentResponse> GetDoctorAppointmentsForDayAsync(int centerId, int userId, DateOnly? date)
        {
            if (date != null && date < DateOnly.FromDateTime(DateTime.UtcNow))
                throw new BusinessException("Date.Invalid");

            date ??= DateOnly.FromDateTime(DateTime.UtcNow);

            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId && c.CenterStatus == CenterStatus.Active);
            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var doctor = await db.Doctors
                .Include(u => u.user)
                .FirstOrDefaultAsync(d => d.userId == userId && d.user.isActive);
            if (doctor == null)
                throw new BusinessException("Doctor.NotFound");

            var appointments = await db.Appointments
                .Where(a => a.doctorId == doctor.Id
                       && a.appointmentDate == date
                       && a.appointmentStatus == AppointmentStatus.Confirmed)
                .OrderBy(a => a.timeSlot)
                .Select(a => new AppointmentSummaryDto
                {
                    appointmentId = a.Id,
                    patientId = a.patientId,
                    patientName = $"{a.Patient.user.firstName} {a.Patient.user.lastName}",
                    date = a.appointmentDate,
                    timeSlot = (TimeOnly) a.timeSlot,
                })
                .ToListAsync();

            return new DoctorAppointmentResponse 
            {
                appointments = appointments
            };
        }
    }
}
