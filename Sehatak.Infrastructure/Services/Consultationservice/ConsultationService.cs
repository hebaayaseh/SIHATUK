using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.ConsultationDto;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.Interfaces.ConsultaionInterface;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Infrastructure.Services.Consultationservice
{
    public class ConsultationService : IConsultation
    {
        private readonly SharedDbContext sharedDbContext;
        private readonly TenantDbContextFactory contextFactory;
        public ConsultationService(SharedDbContext sharedDbContext , TenantDbContextFactory contextFactory)
        {
            this.sharedDbContext = sharedDbContext;
            this.contextFactory = contextFactory;
        }

        public Task<string> ConsultationPayment(int centerId, int consultationId, int userId)
        {
            throw new NotImplementedException();
        }

        public async Task<string> ConsultationRequest(int centerId, int doctorId, int userId)
        {
            var center = await sharedDbContext.MedicalCenters
                 .FirstOrDefaultAsync(c => c.Id == centerId
                                      && c.CenterStatus == CenterStatus.Active);
            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var doctor = await db.Doctors
                .Include(u => u.user)
                .Where(d => d.Id == doctorId
                       && d.user.isActive
                       && d.OnlineEnabled)
                .FirstOrDefaultAsync();

            if (doctor == null)
                throw new BusinessException("Doctor.NotFound");

            var patient = await db.Patients
                .Include(u => u.user)
                .Where(p => p.userId == userId
                       && p.user.isActive)
                .FirstOrDefaultAsync();

            if (patient == null)
                throw new BusinessException("Patient.NotFound");

            var hasPendingRequest = await db.Consultations
               .AnyAsync(c => c.DoctorId == doctorId
                         && c.PatientId == patient.patientId
                         && c.Status == ConsultationStatus.Pending);

            if (hasPendingRequest)
                throw new BusinessException("Consultation.AlreadyRequested");

            await db.Consultations
                .AddAsync(new Consultation
            {
                DoctorId = doctor.Id,
                PatientId = patient.patientId,
                Status = ConsultationStatus.Pending,

            });
            await db.Notifications
                .AddAsync(new Notification
                {
                    UserId = (int)patient.userId,
                    Message = "تم ارسال طلب الاستشارة الى الطبيب , سيصلك الموعد من قبل الطبيب عند الموافقة على الطلب" ,
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow,
                    Type = NotificationType.Appointment
                   
                });
            await db.SaveChangesAsync();
            return "تم ارسال طلب الاستشارة الى الطبيب بانتظار موافقة الطبيب.";

        }

        public async Task<List<DoctorEnableResponse>> GetDoctorEnableConsultation(int centerId)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);
            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            return await db.Doctors
                .Where(d => d.user.isActive
                       && d.OnlineEnabled)
                .Select(r => new DoctorEnableResponse {
                    doctorId = r.Id,
                    doctorName = $"{r.user.firstName} {r.user.lastName}",
                    Bio = r.Bio,
                    depatrmentName = r.department.Name,
                    Specialization = r.Specialization,
                    profileImage = r.user.ProfileImageUrl != null ? r.user.ProfileImageUrl :  null,
                }).ToListAsync();

        }

        public async Task<ConsultationResponse?> ViewConsultation(int centerId, int doctorId, int userId)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var doctor = await db.Doctors
                .Include(u => u.user)
                .FirstOrDefaultAsync(d => d.Id == doctorId
                                     && d.user.isActive);

            if (doctor == null)
                throw new BusinessException("Doctor.NotFound");

            var patient = await db.Patients
                .Include(u => u.user)
                .FirstOrDefaultAsync(p => p.userId == userId
                                     && p.user.isActive);

            if (patient == null)
                throw new BusinessException("Patient.NotFound");

            return await db.Consultations
                .Where(c => c.DoctorId == doctor.Id
                                     && c.PatientId == patient.patientId)
                .Select(p => new ConsultationResponse
                {
                    Id = p.Id,
                    Status = p.Status,
                    PaymentStatus = p.Payment.Status 
                }).FirstOrDefaultAsync();

        }

        public Task<List<ConsultationResponse>> ViewConsultations(int centerId, int userId)
        {
            throw new NotImplementedException();
        }
    }
}
