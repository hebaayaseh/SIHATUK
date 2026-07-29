using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.ConsultationDto;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.DTOs.PaymentDto;
using Sehatak.Application.Interfaces.ConsultaionInterface;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.PaymentEnums;
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

        public Task<bool> ConfirmPaymentAsync(int paymentId, int doctorId)
        {
            throw new NotImplementedException();
        }

        public async Task<string> ConsultationRecordPayment(int centerId, int consultationId, int userId , PaymentRequestDto request)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);
            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var patient = await db.Patients
                .Include(u => u.user)
                .FirstOrDefaultAsync(p => p.userId == userId
                       && p.user.isActive);

            if (patient == null)
                throw new BusinessException("Patient.NotFound");

            var consultaion = await db.Consultations
                .Include(p=>p.Patient)
                .FirstOrDefaultAsync(c => c.Id == consultationId
                                     && c.PatientId == patient.patientId
                                     && c.Status == ConsultationStatus.Pending);

            if (consultaion == null)
                throw new BusinessException("Consultation.NotFound");

            var paymentExists = await db.Consultations
                .AnyAsync(c => c.Id == request.ConsultationId);

            if (paymentExists)
                throw new BusinessException("Payment.Exists");

            string? receiptImageUrl = null;
            if (request.ReceiptImageUrl != null)
            {
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".pdf" };
                var extension = Path.GetExtension(request.ReceiptImageUrl.FileName).ToLower();
                if (!allowedExtensions.Contains(extension))
                    throw new BusinessException("Validation.InvalidFileType");

                if (request.ReceiptImageUrl.Length > 5 * 1024 * 1024)
                    throw new BusinessException("Validation.FileTooLarge");

                var webRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                var uploadsFolder = Path.Combine(webRoot, "uploads", "receipts");
                Directory.CreateDirectory(uploadsFolder);

                var fileName = $"{Guid.NewGuid()}{Path.GetExtension(request.ReceiptImageUrl.FileName)}";
                var filePath = Path.Combine(uploadsFolder, fileName);

                using var stream = new FileStream(filePath, FileMode.Create);
                await request.ReceiptImageUrl.CopyToAsync(stream);

                receiptImageUrl = $"/uploads/receipts/{fileName}";
            }

            var payment = new Payment
            {
                ConsultationId = request.ConsultationId,
                ReceiptImageUrl = receiptImageUrl,
                PaidAt = DateTime.UtcNow,
                Method = PaymentMethod.online,
                Type = PaymentType.Consultation,
                ReferenceNumber = request.ReferenceNumber,
                Status = PaymentStatus.Pending,
                Notes = request.Notes,
                RecordedByStaffId = null

            };
            await db.Payments.AddAsync(payment);
            await db.SaveChangesAsync();
            return "تم تقديم طلب الدفع بنجاح.";

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
                .FirstOrDefaultAsync(p => p.userId == userId
                       && p.user.isActive);
                
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
                    PaymentStatus = p.Payment != null ? p.Payment.Status : (PaymentStatus?)null,
                }).FirstOrDefaultAsync();

        }

        public async Task<List<ConsultationResponse>> ViewConsultations(int centerId, int userId ,  ConsultationStatus status)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var patient = await db.Patients
                .Include(u => u.user)
                .FirstOrDefaultAsync(p => p.userId == userId
                                     && p.user.isActive);

            if (patient == null)
                throw new BusinessException("Patient.NotFound");

            return await db.Consultations
                .Where(c => c.PatientId == patient.patientId
                       && c.Status == status)
                .Select(p => new ConsultationResponse
                {
                    Id = p.Id,
                    Status = p.Status,
                    PaymentStatus = p.Payment != null ? p.Payment.Status : (PaymentStatus?)null,
                }).ToListAsync();
        }
    }
}
