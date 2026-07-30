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

        public async Task<string> CancelConsultaion(int centerId, int userId, int consultationId)
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

            var consultation = await db.Consultations
                .Include(d=>d.Doctor)
                .ThenInclude(u=>u.user)
                .FirstOrDefaultAsync(c => c.Id == consultationId
                                     && c.PatientId == patient.patientId);

            if (consultation == null)
                throw new BusinessException("Consultation.NotFound");

            if (consultation.Status != ConsultationStatus.Pending)
                throw new BusinessException("Consultation.CannotCancelAfterConfirmed");

            var hasPayment = await db.Payments
                .AnyAsync(p => p.ConsultationId == consultationId);

            if (hasPayment)
                throw new BusinessException("Consultation.CannotCancelAfterPaymentSubmitted");


            consultation.Status = ConsultationStatus.Cancelled;

            db.Notifications.Add(new Notification
            {
                UserId = consultation.Doctor.user.Id,
                Message = "قام المريض بإلغاء طلب الاستشارة.",
                Type = NotificationType.Cancellation,
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();

            return "تم الغاء الموعد بنجاح.";
        }

        public async Task<string> CompleteConsultation(int centerId, int userId, int consultationId)
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

            
            var consultation = await db.Consultations
                .Include(p => p.Patient)
                .ThenInclude(u => u.user)
                .FirstOrDefaultAsync(c => c.Id == consultationId
                                    && c.DoctorId == doctor.Id
                                    && c.Status == ConsultationStatus.Accepted);

            if (consultation == null)
                throw new BusinessException("Consultation.NotFound");

            consultation.Status = ConsultationStatus.Completed;

            db.Notifications.Add(new Notification
            {
                UserId = (int)consultation.Patient.userId,
                IsRead = false,
                CreatedAt = DateTime.UtcNow,
                Message = "تم انهاء استشارتك , يمكنك مراجعة ملفك الطبي لمتابعة التفاصيل.",
                Type = NotificationType.Appointment

            });

            await db.SaveChangesAsync();
            return "تم انهاء الاستشارة بنجاح.";

        }

        public async Task<bool> ConfirmPaymentAsync(int centerId ,int paymentId, int doctorId , DateTime ScheduledAt , string videoLink)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var doctor = await db.Doctors
                .Include(u => u.user)
                .FirstOrDefaultAsync(d => d.userId == doctorId);

            if (doctor == null)
                throw new BusinessException("Doctor.NotFound");

            var payment = await db.Payments
                .Include(p=>p.Patient)
                .Include(c=>c.Consultation)
                .FirstOrDefaultAsync(p => p.Id == paymentId);

            if (payment == null)
                throw new BusinessException("Payment.NotFound");

            if (payment.Type != PaymentType.Consultation || payment.Consultation == null)
                throw new BusinessException("Consultation.NotFound");

            if (payment.Consultation.DoctorId != doctor.Id) 
                throw new BusinessException("Auth.Forbidden");

            if (payment.RecordedByStaffId != null)
                throw new BusinessException("Payment.AlreadyConfirmed");

            if (payment.Status != PaymentStatus.Pending)
                throw new BusinessException("Payment.AlreadyProcessed");

            var patient = payment.Patient;

            payment.RecordedByStaffId = doctor.user.Id;
            payment.Consultation.Status = ConsultationStatus.Accepted;
            payment.Status = PaymentStatus.Paid;
            payment.Consultation.ScheduledAt = ScheduledAt;
            payment.Consultation.VideoLink = videoLink;

            await db.Notifications.AddAsync( new Notification
            {
                UserId = (int)patient.userId,
                Type = NotificationType.Appointment,
                IsRead = false,
                CreatedAt=DateTime.UtcNow,
                Message = $"تم الموافقة على الاستشارة عند الطبيب {doctor.user.firstName} {doctor.user.lastName} في الموعد {ScheduledAt}"
            });

            await db.SaveChangesAsync();
            return true;

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

            var paymentExists = await db.Payments
                .AnyAsync(p => p.ConsultationId == request.ConsultationId);

            if (paymentExists)
                throw new BusinessException("Payment.Exists");

            var servicePrice = await db.ServicePrices
              .FirstOrDefaultAsync(s => s.Type == ServiceType.ConsultationCost && s.IsActive);
            if (servicePrice == null)
                throw new BusinessException("ServicePrice.NotFound");

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
                PatientId = patient.patientId,
                ConsultationId = request.ConsultationId,
                ReceiptImageUrl = receiptImageUrl,
                Amount = servicePrice.Price,
                PaidAt = DateTime.UtcNow,
                Method = PaymentMethod.online,
                Type = PaymentType.Consultation,
                ReferenceNumber = request.ReferenceNumber,
                Status = PaymentStatus.Pending,
                Notes = request.Notes,
                RecordedByStaffId = null,
                
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

        public async Task<List<PaymentResponseDto>> GetPaymentPinding(int centerId, int doctorId)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId && c.CenterStatus == CenterStatus.Active);
            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var doctor = await db.Doctors
                .FirstOrDefaultAsync(d => d.userId == doctorId 
                                     && d.user.isActive);

            if (doctor == null)
                throw new BusinessException("Doctor.NotFound");

            return await db.Payments
                .Where(p => p.ConsultationId != null
                    && p.Consultation.DoctorId == doctor.Id
                    && p.Consultation.Status == ConsultationStatus.Pending
                    && p.Status == PaymentStatus.Pending)
                .Select(n => new PaymentResponseDto
                {
                    Id = n.Id,
                    patientId = n.PatientId,
                    PaidAt = n.PaidAt,
                    ReceiptImageUrl = n.ReceiptImageUrl,
                    ReferenceNumber = n.ReferenceNumber
                }).ToListAsync();
        }

        public async Task<PaymentResponseDto> GetPaymentPinding(int centerId, int doctorId, int paymentId)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId && c.CenterStatus == CenterStatus.Active);
            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var doctor = await db.Doctors
                .FirstOrDefaultAsync(d => d.userId == doctorId 
                                    && d.user.isActive);

            if (doctor == null)
                throw new BusinessException("Doctor.NotFound");

            var payment = await db.Payments
                .Where(p => p.Id == paymentId
                    && p.ConsultationId != null
                    && p.Consultation.DoctorId == doctor.Id
                    && p.Consultation.Status == ConsultationStatus.Pending
                    && p.Status == PaymentStatus.Pending)
                .Select(n => new PaymentResponseDto
                {
                    Id = n.Id,
                    patientId = n.PatientId,
                    PaidAt = n.PaidAt,
                    ReceiptImageUrl = n.ReceiptImageUrl,
                    ReferenceNumber = n.ReferenceNumber
                }).FirstOrDefaultAsync();

            if (payment == null)
                throw new BusinessException("Payment.NotFound");

            return payment;
        }

        public async Task<string> RejectConsultationPaymentAsync(int centerId, int paymentId, int doctorId, string rejectionReason)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                         && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var doctor = await db.Doctors
                .Include(u => u.user)
                .FirstOrDefaultAsync(d => d.userId == doctorId
                                     && d.user.isActive);

            if (doctor == null)
                throw new BusinessException("Doctor.NotFound");

            var payment = await db.Payments
               .Include(p => p.Patient)
               .Include(p => p.Consultation)
               .FirstOrDefaultAsync(p => p.Id == paymentId);

            if (payment == null)
                throw new BusinessException("Payment.NotFound");

            if (payment.Type != PaymentType.Consultation || payment.Consultation == null)
                throw new BusinessException("Consultation.NotFound");

            if (payment.Consultation.DoctorId != doctor.Id) 
                throw new BusinessException("Auth.Forbidden");

            if (payment.Status != PaymentStatus.Pending)
                throw new BusinessException("Payment.AlreadyProcessed");

            payment.Status = PaymentStatus.Failed;
            payment.RecordedByStaffId = doctor.user.Id;
            payment.Notes = rejectionReason;

            await db.Notifications.AddAsync(new Notification
            {
                UserId = (int)payment.Patient.userId,
                Type = NotificationType.Appointment,
                IsRead = false,
                CreatedAt = DateTime.UtcNow,
                Message = "تم رفض إيصال الدفع، يرجى التأكد من البيانات وإعادة الإرسال."
            });

            await db.SaveChangesAsync();
            return "تم رفض الدفعة.";
        }

        public async Task<string> RejectConsultationRequestAsync(int centerId, int consultationId, int doctorId, string rejectionReason)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var doctor = await db.Doctors
                .Include(u => u.user)
                .FirstOrDefaultAsync(d => d.userId == doctorId
                                     && d.user.isActive);

            if (doctor == null)
                throw new BusinessException("Doctor.NotFound");


            var consultation = await db.Consultations
                .Include(p=>p.Patient)
                .FirstOrDefaultAsync(c => c.Id == consultationId
                                    && c.DoctorId == doctor.Id
                                    && c.Status == ConsultationStatus.Pending);

            if (consultation == null)
                throw new BusinessException("Consultation.NotFound");

            consultation.Status = ConsultationStatus.Rejected;
            consultation.Notes = rejectionReason;

            await db.Notifications.AddAsync(new Notification
            {
                UserId = (int)consultation.Patient.userId,
                Type = NotificationType.Appointment,
                IsRead = false,
                CreatedAt = DateTime.UtcNow,
                Message = "نأسف، تم رفض طلب الاستشارة من قبل الطبيب."
            });

            await db.SaveChangesAsync();
            return "تم رفض طلب الاستشارة.";

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
