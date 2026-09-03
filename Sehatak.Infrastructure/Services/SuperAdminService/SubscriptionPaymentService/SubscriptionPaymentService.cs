using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.Extensions.Hosting;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.DTOs.RecordPaymentRequestDto;
using Sehatak.Application.Interfaces.IEmail;
using Sehatak.Application.Interfaces.ISubscriptionPaymentService;
using Sehatak.Domain.Entities.SharedEntities;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Infrastructure.Services.SuperAdminService.SubscriptionPaymentService
{
    public class SubscriptionPaymentService : ISubscriptionPayment
    {
        private readonly SharedDbContext sharedDbContext;
        private readonly IHostEnvironment _env;
        private readonly IEmailService _emailService;

        public SubscriptionPaymentService(SharedDbContext sharedDb, IHostEnvironment env, IEmailService emailService)
        {
            sharedDbContext = sharedDb;
            _env = env;
            _emailService = emailService;
        }
        public async Task<bool> ConfirmPaymentAsync(int paymentId, int superAdminId)
        {
            var payment = await sharedDbContext.subscriptionPayments
                .Include(p => p.Subscription)
                .FirstOrDefaultAsync(p => p.Id == paymentId);

            if (payment == null)
                throw new BusinessException("Payment.NotFound");

            if (payment.RecordedBySuperAdminId != null)
                throw new BusinessException("Payment.AlreadyConfirmed");

            if (payment.Subscription == null)
                throw new BusinessException("Subscription.NotFound");

            var subscription = payment.Subscription;

            if (subscription.Status != SubscriptionStatus.Pending)
                throw new BusinessException("Subscription.AlreadyProcessed");

            var center = await sharedDbContext.MedicalCenters.FindAsync(subscription.CenterId);
            if (center == null)
                throw new BusinessException("Center.NotFound");

            payment.RecordedBySuperAdminId = superAdminId;

            subscription.Status = SubscriptionStatus.Active;

            var oldFeature = await sharedDbContext.CenterFeatures
                .Where(c => c.CenterId == center.Id)
                .ToListAsync();
            sharedDbContext.CenterFeatures.RemoveRange(oldFeature);

            var newFeatureIds = await sharedDbContext.PlanFeatures
                .Where(c => c.PlanId == subscription.PlanId)
                .Select(f => f.FeatureId)
                .ToListAsync();

            foreach (var feature in newFeatureIds)
            {
                sharedDbContext.CenterFeatures.Add(new CenterFeature
                {
                    CenterId = center.Id,
                    FeatureId = feature,
                    IsEnabled = true
                });
            }

            if (center.CenterStatus == CenterStatus.Suspended)
                center.CenterStatus = CenterStatus.Active;

            await sharedDbContext.SaveChangesAsync();

            if (!string.IsNullOrEmpty(center.AdminEmail))
            {
                await _emailService.SendPaymentConfirmedAsync(
                    center.AdminEmail,
                    center.Name,
                    payment.Amount
                );
            }

            return true;

        }

        public async Task<Application.Common.PagedResult<PaymentResponseDto>> GetCenterPaymentsAsync(int centerId,PagedRequest request)
        {
            var center = await sharedDbContext.MedicalCenters.FirstOrDefaultAsync(c => c.Id == centerId);
            if (center == null)
                throw new BusinessException("Center.NotFound");

            var query = sharedDbContext.subscriptionPayments
                .Include(c => c.Center)
                .Include(s => s.RecordedBy)
                .Where(c => c.CenterId == centerId)
                .OrderByDescending(p => p.PaidAt)
                .Select(p => new PaymentResponseDto
                {
                    Id = p.Id,
                    CenterId = p.CenterId,
                    CenterName = p.Center!.Name,
                    SubscriptionId = p.SubscriptionId,
                    Amount = p.Amount,
                    PaymentMethod = p.PaymentMethod,
                    ReferenceNumber = p.ReferenceNumber,
                    ReceiptImageUrl = p.ReceiptImageUrl,
                    PaidAt = p.PaidAt,
                    RecordedBy = p.RecordedBy != null ? p.RecordedBy.Name : "Pending",
                    Notes = p.Notes,
                    IsConfirmed = p.RecordedBySuperAdminId != null

                });
            return await query.ToPagedResultAsync(request.PageNumber, request.PageSize);


        }

        public async Task<Application.Common.PagedResult<PaymentResponseDto>> GetPendingPaymentsAsync(PagedRequest request)
        {

            var query = sharedDbContext.subscriptionPayments
            .Include(p => p.Center)
            .Include(p => p.Subscription).ThenInclude(s => s.Plan)
            .Where(p => p.RecordedBySuperAdminId == null && p.RequestId == null)
            .OrderByDescending(p => p.PaidAt)
            .Select(p => new PaymentResponseDto
            {
                Id = p.Id,
                CenterId = p.CenterId,
                CenterName = p.Center!.Name,
                SubscriptionId = p.SubscriptionId,
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

        public async Task<PaymentResponseDto> RecordPaymentAsync(PaymentRequestExist request, int centerId)
        {
            var center = await sharedDbContext.MedicalCenters.FindAsync(centerId);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            var supscriptionCenter = await sharedDbContext.CenterSubscriptions
                .Include(c => c.Plan)
                .FirstOrDefaultAsync(c => c.Id == request.SubscriptionId
                                     && c.CenterId == centerId
                                     && c.Status == SubscriptionStatus.Pending);

            if (supscriptionCenter == null)
                throw new BusinessException("Subscription.NotFound");

            var paymentExists = await sharedDbContext.subscriptionPayments
           .AnyAsync(p => p.SubscriptionId == request.SubscriptionId);

            if (paymentExists)
                throw new BusinessException("Payment.Exists");

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
                CenterId = centerId,
                SubscriptionId = request.SubscriptionId,
                Amount = supscriptionCenter.Plan.Price,
                PaymentMethod = request.PaymentMethod,
                ReferenceNumber = request.ReferenceNumber,
                ReceiptImageUrl = receiptImageUrl,
                Notes = request.Notes,
                PaidAt = DateTime.UtcNow,
                RecordedBySuperAdminId = null

            };


            await sharedDbContext.subscriptionPayments.AddAsync(payment);
            await sharedDbContext.SaveChangesAsync();

            return new PaymentResponseDto
            {
                Id = payment.Id,
                CenterId = payment.CenterId,
                CenterName = center.Name,
                SubscriptionId = payment.SubscriptionId,
                Amount = payment.Amount,
                PaymentMethod = payment.PaymentMethod,
                ReferenceNumber = payment.ReferenceNumber,
                ReceiptImageUrl = payment.ReceiptImageUrl,
                PaidAt = payment.PaidAt,
                Notes = payment.Notes,
                IsConfirmed = false
            };

        }
    }
}