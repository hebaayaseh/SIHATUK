using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.Email;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.Interfaces.IEmail;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;

namespace Sehatak.Infrastructure.Services
{
    public class AdminBulkEmailService : IAdminBulkEmailService
    {
        private readonly SharedDbContext sharedDbContext;
        private readonly IEmailService emailService;

        public AdminBulkEmailService(SharedDbContext sharedDbContext, IEmailService emailService)
        {
            this.sharedDbContext = sharedDbContext;
            this.emailService = emailService;
        }

        public async Task<int> SendAsync(SendEmailDto request)
        {
            List<string> emails;
            var target = request.Target?.Trim().ToLowerInvariant();

            switch (target)
            {
                case "specific":
                    if (request.CenterId == null)
                        throw new BusinessException("Validation.CenterIdRequired");

                    var center = await sharedDbContext.MedicalCenters
                        .FirstOrDefaultAsync(c => c.Id == request.CenterId);

                    if (center == null)
                        throw new BusinessException("Center.NotFound");

                    emails = !string.IsNullOrEmpty(center.AdminEmail)
                        ? new List<string> { center.AdminEmail }
                        : new List<string>();
                    break;

                case "active":
                    emails = await sharedDbContext.MedicalCenters
                        .Where(c => c.CenterStatus == CenterStatus.Active && c.AdminEmail != null)
                        .Select(c => c.AdminEmail!)
                        .ToListAsync();
                    break;

                case "expired":
                    emails = await sharedDbContext.CenterSubscriptions
                        .Where(s => s.Status == SubscriptionStatus.Expired
                                 && s.Center.AdminEmail != null
                                 && !sharedDbContext.CenterSubscriptions.Any(a => a.CenterId == s.CenterId
                                                                               && a.Status == SubscriptionStatus.Active))
                        .Select(s => s.Center.AdminEmail!)
                        .Distinct()
                        .ToListAsync();
                    break;

                default:
                    throw new BusinessException("Validation.InvalidTarget");
            }
            emails = emails.Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e.Trim()).Distinct().ToList();

            if (emails.Count == 0)
                return 0;

            await emailService.SendBulkAsync(emails, request.Subject, request.Message);
            return emails.Count;
        }
    }
}