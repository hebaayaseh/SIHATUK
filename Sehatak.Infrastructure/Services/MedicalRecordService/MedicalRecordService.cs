using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.DTOs.MedicalRecordDto;
using Sehatak.Application.Interfaces;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Infrastructure.Services.MedicalRecordService
{
    public class MedicalRecordService : IMedicalRecord
    {
        private readonly SharedDbContext sharedDbContext;
        private readonly TenantDbContextFactory contextFactory;
        public MedicalRecordService(SharedDbContext sharedDbContext, TenantDbContextFactory contextFactory)
        {
            this.sharedDbContext = sharedDbContext;
            this.contextFactory = contextFactory;
        }

        public async Task<MedicalRecordResponseDto> AddMedicalRecordaSYNC(int centerId,int userId, MedicalReqordRequestDto request)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var doctor = await db.Doctors
                .Include(u => u.user)
                .FirstOrDefaultAsync(d => d.userId == userId
                                     && d.user.isActive);

            if (doctor == null)
                throw new BusinessException("Doctor.NotFound");

            if (request.AppointmentId == null && request.ConsultationId == null)
                throw new BusinessException("MedicalRecord.MustLinkToAppointmentOrConsultation");

            if (request.AppointmentId != null && request.ConsultationId != null)
                throw new BusinessException("MedicalRecord.CannotLinkToBoth");

            if(request.AppointmentId!=null)
            {
                var appointment = await db.Appointments
                    .FirstOrDefaultAsync(d => d.Id == request.AppointmentId
                                         && d.doctorId == doctor.Id);
                if (appointment == null)
                    throw new BusinessException("Appointment.NotFound");
            }
            else
            {
                var consultation = await db.Consultations
                   .FirstOrDefaultAsync(c => c.Id == request.ConsultationId
                                        && c.DoctorId == doctor.Id
                                        && c.Status == ConsultationStatus.Accepted);
                 if (consultation == null)
                    throw new BusinessException("Consultation.NotFound");
            }
            var record = new MedicalRecord
            {
                PatientId = request.PatientId,
                DoctorId = doctor.Id,
                AppointmentId = request.AppointmentId,
                ConsultationId = request.ConsultationId,
                Prescription = request.Prescription,
                Notes = request.Notes,
                Diagnosis = request.Diagnosis,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await db.MedicalRecords.AddAsync(record);
            await db.SaveChangesAsync();

            return new MedicalRecordResponseDto 
            { 
                Diagnosis = record.Diagnosis,
                Notes = record.Notes,
                Prescription = record.Prescription,
                
            };

        }
    }
}
