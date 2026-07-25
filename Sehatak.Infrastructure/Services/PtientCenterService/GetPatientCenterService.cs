using DocumentFormat.OpenXml.Office2016.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.DTOs.PatientCenter;
using Sehatak.Application.Interfaces.ApointmentInterface;
using Sehatak.Application.Interfaces.IPatientCenter;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
                .Include(u => u.user)
                .Where(p => p.userId == request.userId)
                .FirstOrDefaultAsync();

            if (patient == null)
                throw new BusinessException("Patient.NotFound");

            var appointments = await db.Appointments
                        .Include(p => p.Patient)
                        .ThenInclude(u => u.user)
                        .Where(p => p.patientId == patient.patientId
                               && p.appointmentStatus == request.status)
                        .Select(p => new PatientSummaryDto
                        {
                            timeSlot = (TimeOnly)p.timeSlot,
                            date = p.appointmentDate,
                            status = p.appointmentStatus,
                            DoctorName = $"{p.Doctor.user.firstName} {p.Doctor.user.lastName}",
                        }).ToListAsync();


            return new GetPatientResponseDto
            {
                Id = patient.patientId,
                pateintName = $"{patient.user.firstName} {patient.user.lastName}",
                appointments = appointments

            };


        }

        public async Task<List<GetPatientResponseDto>> GetPatientesAsync(int centerId,AppointmentStatus status)
        {
            var center = await SharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            return await db.Users
                .Where(u => u.role == userRole.Patient && u.patient != null)
                .Select(u => new GetPatientResponseDto
                {
                    Id = u.Id,
                    pateintName = u.firstName+" "+u.lastName,
                    appointments = db.Appointments
                        .Include(p => p.Patient)
                        .ThenInclude(u => u.user)
                        .Where(p => p.patientId == u.patient.patientId
                               && p.appointmentStatus == status)
                        .Select(p => new PatientSummaryDto
                        {
                            timeSlot = (TimeOnly)p.timeSlot,
                            date = p.appointmentDate,
                            status = p.appointmentStatus,
                            DoctorName = $"{p.Doctor.user.firstName} {p.Doctor.user.lastName}",
                        }).ToList()

                }).ToListAsync();
        }

    }
}
