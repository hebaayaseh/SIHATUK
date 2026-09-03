using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.CreateCenterRequestDto;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.DTOs.RecordPaymentRequestDto;
using Sehatak.Application.Interfaces.CenterRegistrationRequest;
using Sehatak.Application.Interfaces.IEmail;
using Sehatak.Domain.Entities.SharedEntities;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;
using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Infrastructure.Services.SuperAdminService.CenterService
{
    public class CenterRegistirationRequestService : ICenterRegistration
    {
        private readonly SharedDbContext sharedDbContext;
        private TenantDbContextFactory contextFactory;
        private readonly IEmailService emailService;
        public CenterRegistirationRequestService(SharedDbContext sharedDbContext, TenantDbContextFactory contextFactory, IEmailService emailService)
        {
            this.sharedDbContext = sharedDbContext;
            this.contextFactory = contextFactory;
            this.emailService = emailService;
        }

        public async Task<CenterRegistrationResponseDto> CenterRequestAsync(CenterRegistrationRequestDto request)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Name == request.CenterName);

            if (center != null)
                throw new BusinessException("Center.UrlExists");

            var plan = await sharedDbContext.SubscriptionPlans.FindAsync(request.PlanId);
            if (plan == null)
                throw new BusinessException("Subscription.PlanNotFound");

            var pendingRequestExists = await sharedDbContext.centerRegistrationRequests
                .AnyAsync(r => r.AdminEmail == request.AdminEmail
                            && r.Status == CenterRegistrationStatus.Pending);
            if (pendingRequestExists)
                throw new BusinessException("CenterRegistration.AlreadyPending");


            string? receiptImageUrl = null;
            if (request.LogoUrl != null)
            {
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".pdf" };
                var extension = Path.GetExtension(request.LogoUrl.FileName).ToLower();
                if (!allowedExtensions.Contains(extension))
                    throw new BusinessException("Validation.InvalidFileType");

                if (request.LogoUrl.Length > 5 * 1024 * 1024)
                    throw new BusinessException("Validation.FileTooLarge");

                var webRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                var uploadsFolder = Path.Combine(webRoot, "uploads", "receipts");
                Directory.CreateDirectory(uploadsFolder);

                var fileName = $"{Guid.NewGuid()}{Path.GetExtension(request.LogoUrl.FileName)}";
                var filePath = Path.Combine(uploadsFolder, fileName);

                using var stream = new FileStream(filePath, FileMode.Create);
                await request.LogoUrl.CopyToAsync(stream);

                receiptImageUrl = $"/uploads/receipts/{fileName}";

            }

            var creatCenter = new CenterRegistrationRequest
            {
                AdminEmail = request.AdminEmail,
                AdminPhone = request.AdminPhone,
                CenterAddress = request.CenterAddress,
                CenterName = request.CenterName,
                CenterPhone = request.CenterPhone,
                AdminFirstName = request.AdminFirstName,
                AdminLastName = request.AdminLastName,
                PlanId = request.PlanId,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.PasswordHash),
                RequestedAt = request.RequestedAt,

            };
            await sharedDbContext.centerRegistrationRequests.AddAsync(creatCenter);
            await sharedDbContext.SaveChangesAsync();

            await emailService.SendCustomMessageAsync(
                request.AdminEmail, "Success Request / SIHATUAK ", "تم الطلب بنجاح، يرجى إتمام الدفع لمتابعة إجراءات التسجيل."
                );

            return new CenterRegistrationResponseDto
            {
                Id = creatCenter.Id,
                CenterName = creatCenter.CenterName,
                CenterPhone = creatCenter.CenterPhone,
                CenterAddress = creatCenter.CenterAddress,
                AdminEmail = creatCenter.AdminEmail,
                AdminFirstName = creatCenter.AdminFirstName,
                AdminLastName = creatCenter.AdminLastName,
                PlanId = creatCenter.PlanId,
                AdminPhone = creatCenter.AdminPhone,
                Status = CenterRegistrationStatus.Pending,
                RequestedAt = creatCenter.RequestedAt,
                ReviewedAt = null,
                RejectionReason = null,
            };
        }

        public async Task<bool> RecordRegistrationPaymentAsync(int requestId, recordPaymentRequestDto request)
        {
            var registrationRequest = await sharedDbContext.centerRegistrationRequests
                .FirstOrDefaultAsync(r => r.Id == requestId);

            if (registrationRequest == null)
                throw new BusinessException("CenterRegistration.NotFound");

            if (registrationRequest.Status != CenterRegistrationStatus.Pending)
                throw new BusinessException("CenterRegistration.AlreadyReviewed");

            var paymentExists = await sharedDbContext.subscriptionPayments
                .AnyAsync(p => p.RequestId == requestId);

            if (paymentExists)
                throw new BusinessException("Payment.Exists");

            var plan = await sharedDbContext.SubscriptionPlans.FindAsync(registrationRequest.PlanId);
            if (plan == null)
                throw new BusinessException("Subscription.PlanNotFound");

            string? receiptImageUrl = null;
            if (request.ReceiptImage != null)
            {
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".pdf" };
                var extension = Path.GetExtension(request.ReceiptImage.FileName).ToLower();
                if (!allowedExtensions.Contains(extension))
                    throw new BusinessException("Validation.InvalidFileType");

                if (request.ReceiptImage.Length > 5 * 1024 * 1024)
                    throw new BusinessException("Validation.FileTooLarge");

                var webRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                var uploadsFolder = Path.Combine(webRoot, "uploads", "receipts");
                Directory.CreateDirectory(uploadsFolder);

                var fileName = $"{Guid.NewGuid()}{Path.GetExtension(request.ReceiptImage.FileName)}";
                var filePath = Path.Combine(uploadsFolder, fileName);

                using var stream = new FileStream(filePath, FileMode.Create);
                await request.ReceiptImage.CopyToAsync(stream);

                receiptImageUrl = $"/uploads/receipts/{fileName}";
            }

            var payment = new SubscriptionPayment
            {
                RequestId = requestId,
                CenterId = null,
                SubscriptionId = null,
                Amount = plan.Price,
                PaymentMethod = request.PaymentMethod,
                ReferenceNumber = request.ReferenceNumber,
                ReceiptImageUrl = receiptImageUrl,
                Notes = request.Notes,
                PaidAt = DateTime.UtcNow,
                RecordedBySuperAdminId = null
            };

            await sharedDbContext.subscriptionPayments.AddAsync(payment);
            await sharedDbContext.SaveChangesAsync();

            return true;
        }

        public async Task<Application.Common.PagedResult<PaymentResponseDto>> GetPendingRegistrationPaymentsAsync(PagedRequest request)
        {
            var query = sharedDbContext.subscriptionPayments
                .Include(p => p.Request)
                .Where(p => p.RequestId != null && p.RecordedBySuperAdminId == null)
                .OrderByDescending(p => p.PaidAt)
                .Select(p => new PaymentResponseDto
                {
                    Id = p.Id,
                    CenterName = p.Request!.CenterName,
                    Amount = p.Amount,
                    PaymentMethod = p.PaymentMethod,
                    ReferenceNumber = p.ReferenceNumber,
                    ReceiptImageUrl = p.ReceiptImageUrl,
                    PaidAt = p.PaidAt,
                    Notes = p.Notes,
                    IsConfirmed = false
                });

            return await query.ToPagedResultAsync(request.PageNumber, request.PageSize);

        }

        public async Task<bool> ConfirmRegistrationPaymentAsync(int paymentId, int superAdminId)
        {
            var payment = await sharedDbContext.subscriptionPayments
                .Include(p => p.Request)
                .FirstOrDefaultAsync(p => p.Id == paymentId && p.RequestId != null);

            if (payment == null)
                throw new BusinessException("Payment.NotFound");

            if (payment.RecordedBySuperAdminId != null)
                throw new BusinessException("Payment.AlreadyConfirmed");

            if (payment.Request == null || payment.Request.Status != CenterRegistrationStatus.Pending)
                throw new BusinessException("CenterRegistration.AlreadyReviewed");

            payment.RecordedBySuperAdminId = superAdminId;
            await sharedDbContext.SaveChangesAsync();

            return true;
        }

        public async Task<Application.Common.PagedResult<CenterRegistrationResponseDto>> GetCentersRegisterationAsync(PagedRequest request)
        {
            var query = sharedDbContext.centerRegistrationRequests
                .Where(c => c.Status == CenterRegistrationStatus.Pending)
                .OrderByDescending(r => r.RequestedAt)
                .Select(c => new CenterRegistrationResponseDto
                {
                    AdminEmail = c.AdminEmail,
                    AdminFirstName = c.AdminFirstName,
                    AdminLastName = c.AdminLastName,
                    PlanId = c.PlanId,
                    AdminPhone = c.AdminPhone,
                    CenterAddress = c.CenterAddress,
                    CenterName = c.CenterName,
                    CenterPhone = c.CenterPhone,
                    CreatedCenterId = c.CreatedCenterId,
                    RequestedAt = c.RequestedAt,
                    Status = c.Status,
                    RejectionReason = c.RejectionReason

                });

            return await query.ToPagedResultAsync(request.PageNumber, request.PageSize);
        }
        public async Task<CenterRegistrationResponseDto?> GetCenterRegistrationAsync(int centerId)
        {
            var center = await sharedDbContext.centerRegistrationRequests
                .FindAsync(centerId);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            return await sharedDbContext.centerRegistrationRequests
                .Where(c => c.Id == centerId)
                .Select(c => new CenterRegistrationResponseDto
                {
                    AdminEmail = c.AdminEmail,
                    AdminFirstName = c.AdminFirstName,
                    AdminLastName = c.AdminLastName,
                    PlanId = c.PlanId,
                    AdminPhone = c.AdminPhone,
                    CenterAddress = c.CenterAddress,
                    CenterName = c.CenterName,
                    CenterPhone = c.CenterPhone,
                    CreatedCenterId = c.CreatedCenterId,
                    RequestedAt = c.RequestedAt,
                    Status = c.Status,
                    RejectionReason = c.RejectionReason

                }).FirstOrDefaultAsync();
        }

        public async Task<bool> ApproveCenterRequest(int requestId, int superAdminId)
        {
            var superAdmin = await sharedDbContext.SuperAdmins
                .FindAsync(superAdminId);

            if (superAdmin == null)
                throw new BusinessException("Auth.Unauthorized");

            var request = await sharedDbContext.centerRegistrationRequests
                .FirstOrDefaultAsync(r => r.Id == requestId);

            if (request == null)
                throw new BusinessException("Center.NotFound");

            if (request.Status != CenterRegistrationStatus.Pending)
                throw new BusinessException("CenterRegistration.AlreadyReviewed");

            var plan = await sharedDbContext.SubscriptionPlans.FindAsync(request.PlanId);
            if (plan == null)
                throw new BusinessException("Subscription.PlanNotFound");


            var confirmedPayment = await sharedDbContext.subscriptionPayments
                .FirstOrDefaultAsync(p => p.RequestId == requestId && p.RecordedBySuperAdminId != null);

            if (confirmedPayment == null)
                throw new BusinessException("CenterRegistration.PaymentNotConfirmed");

            var newCenter = new MedicalCenter
            {
                Name = request.CenterName,
                AdminEmail = request.AdminEmail,
                Address = request.CenterAddress,
                Phone = request.CenterPhone,
                AdminWhatsappNumber = request.AdminPhone,
                CreatedAt = DateTime.UtcNow,
                CenterStatus = CenterStatus.Active,
                PartialRefundPercent = request.PartialRefundPercent,
                RefundPolicyHours = request.RefundPolicyHours,
                RequiresPrepayment = request.RequiresPrepayment,
                PrepaymentAmount = request.PrepaymentAmount,
                LogoUrl = request.logo
            };
            var centerUrl = $"{GenerateSlug(request.CenterName)}.sehatak.com";
            var urlExists = await sharedDbContext.MedicalCenters
                .AnyAsync(c => c.UniqueUrl == centerUrl);
            if (urlExists)
                throw new BusinessException("Center.UrlExists");

            newCenter.UniqueUrl = centerUrl;

            await sharedDbContext.MedicalCenters.AddAsync(newCenter);
            await sharedDbContext.SaveChangesAsync();

            var subscription = new CenterSubscription
            {
                CenterId = newCenter.Id,
                PlanId = plan.Id,
                StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
                EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(plan.DurationDays)),
                Status = SubscriptionStatus.Active,
                AmountPaid = plan.Price
            };

            await sharedDbContext.CenterSubscriptions.AddAsync(subscription);
            await sharedDbContext.SaveChangesAsync();

            confirmedPayment.CenterId = newCenter.Id;
            confirmedPayment.SubscriptionId = subscription.Id;

            var planFeatures = await sharedDbContext.PlanFeatures
                .Where(pf => pf.PlanId == plan.Id)
                .ToListAsync();

            foreach (var pf in planFeatures)
            {
                sharedDbContext.CenterFeatures.Add(new CenterFeature
                {
                    CenterId = newCenter.Id,
                    FeatureId = pf.FeatureId,
                    IsEnabled = true
                });
            }

            await contextFactory.CreateTenantDatabaseAsync(newCenter.Id);
            newCenter.CenterStatus = CenterStatus.Active;


            request.Status = CenterRegistrationStatus.Approved;
            request.ReviewedAt = DateTime.UtcNow;
            request.ReviewedBySuperAdminId = superAdminId;
            request.CreatedCenterId = newCenter.Id;
            await sharedDbContext.SaveChangesAsync();

            try
            {
                using var db = contextFactory.CreateForCenter(newCenter.Id);
                var emailExists = await db.Users.AnyAsync(u => u.email == request.AdminEmail);
                if (emailExists)
                    throw new BusinessException("Auth.EmailExists");

                var admin = new User
                {
                    firstName = request.AdminFirstName,
                    lastName = request.AdminLastName,
                    address = request.CenterAddress,
                    city = request.CenterAddress,
                    phoneNumber = request.AdminPhone,
                    email = request.AdminEmail,
                    passwordHash = request.PasswordHash,
                    role = userRole.Admin,
                    isActive = true,
                    createdAt = DateTime.UtcNow
                };
                await db.Users.AddAsync(admin);
                await db.SaveChangesAsync();
            }
            catch (Exception ex) when (ex is not BusinessException)
            {
                // فشل إنشاء الأدمن بعد ما المركز والاشتراك انحفظوا فعليًا -
                // نعلّم المركز يحتاج تدخل يدوي بدل ما نسيبه بحالة متناقضة صامتة
                newCenter.CenterStatus = CenterStatus.Suspended;
                await sharedDbContext.SaveChangesAsync();
                throw new BusinessException("CenterRegistration.AdminCreationFailed");
            }

            await emailService.SendCustomMessageAsync(
                request.AdminEmail, "Approve Request / SIHATUAK ", "تم تفعيل حسابك بنجاح !"
            );

            return true;

        }
        public async Task<bool> RejectAsync(int requestId, int superAdminId, string rejectionReason)
        {
            var superAdmin = await sharedDbContext.SuperAdmins.FindAsync(superAdminId);
            if (superAdmin == null)
                throw new BusinessException("Auth.Unauthorized");

            var request = await sharedDbContext.centerRegistrationRequests.FindAsync(requestId);
            if (request == null)
                throw new BusinessException("CenterRegistration.NotFound");

            if (request.Status != CenterRegistrationStatus.Pending)
                throw new BusinessException("CenterRegistration.AlreadyReviewed");

            request.Status = CenterRegistrationStatus.Rejected;
            request.ReviewedAt = DateTime.UtcNow;
            request.ReviewedBySuperAdminId = superAdminId;
            request.RejectionReason = rejectionReason;

            await sharedDbContext.SaveChangesAsync();
            return true;
        }
        private string GenerateSlug(string name)
        {
            return name.Trim().ToLower().Replace(" ", "-");
        }
    }
}