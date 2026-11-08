using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.DTOs.PatientCenter;
using Sehatak.Application.Interfaces.IPatientCenter;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;

namespace Sehatak.Infrastructure.Services.PtientCenterService
{
    public class GetPatientCenterService : IGetpatientCenter
    {
        private readonly SharedDbContext SharedDbContext;
        private readonly TenantDbContextFactory contextFactory;
        public GetPatientCenterService(SharedDbContext sharedDbContext, TenantDbContextFactory contextFactory)
        {
            SharedDbContext = sharedDbContext;
            this.contextFactory = contextFactory;
        }

        public async Task<GetPatientResponseDto> GetPatientAsync(int centerId, GetPatientRequestDto request)
        {
            var center = await SharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);


            var patient = await db.Patients
                .Include(p => p.user)
                .FirstOrDefaultAsync(p => p.patientId == request.patientId);

            if (patient == null)
                throw new BusinessException("Patient.NotFound");

            var appointments = await db.Appointments
                        .Include(p => p.Patient)
                        .ThenInclude(u => u.user)
                        .Where(p => p.patientId == patient.patientId
                               && p.appointmentStatus == request.status)
                        .Select(p => new PatientSummaryDto
                        {
                            timeSlot = p.timeSlot,
                            date = p.appointmentDate,
                            status = p.appointmentStatus,
                            DoctorName = $"{p.Doctor.user.firstName} {p.Doctor.user.lastName}",
                        }).ToListAsync();


            return new GetPatientResponseDto
            {
                Id = patient.patientId,
                pateintName = patient.userId != null
                    ? patient.user.firstName + " " + patient.user.lastName
                    : patient.FirstName + " " + patient.LastName,
                appointments = appointments

            };


        }

        public async Task<PagedResult<GetPatientResponseDto>> GetPatientesAsync(
    int centerId, AppointmentStatus status, PagedRequest request)
        {
            var center = await SharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId 
                                     && c.CenterStatus == CenterStatus.Active);
            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var query = db.Patients
                .Include(p => p.user)
                .Where(u => u.appointments.Any(a => a.appointmentStatus == status))
                .OrderBy(u => u.user.firstName)
                .Select(u => new GetPatientResponseDto
                {
                    Id = u.patientId,
                    pateintName = u.userId != null
                    ? u.user.firstName + " " + u.user.lastName
                    : u.FirstName + " " + u.LastName,
                    appointments = db.Appointments
                        .Where(p => p.patientId == u.patientId
                               && p.appointmentStatus == status)
                        .Select(p => new PatientSummaryDto
                        {
                            timeSlot = p.timeSlot,
                            date = p.appointmentDate,
                            status = p.appointmentStatus,
                            DoctorName = p.Doctor.user.firstName + " " + p.Doctor.user.lastName,
                        }).ToList()
                });

            return await query.ToPagedResultAsync(request.PageNumber, request.PageSize);
        }

    }
}
