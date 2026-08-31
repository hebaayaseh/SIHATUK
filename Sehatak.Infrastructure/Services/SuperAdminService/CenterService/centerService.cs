using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.CentersDto;
using Sehatak.Application.DTOs.CreateCenterRequestDto;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.Interfaces.IEmail;
using Sehatak.Application.Interfaces.MedicalCenter;
using Sehatak.Domain.Entities.SharedEntities;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Infrastructure.Services.SuperAdminService.CenterService
{
    public class centerService : ICenter
    {
        private readonly SharedDbContext sharedDbContext;
        private readonly TenantDbContextFactory tenantDbContextFactory;
        private readonly IEmailService _emailService;
        public centerService(SharedDbContext sharedDbContext, TenantDbContextFactory tenantDbContextFactory, IEmailService _emailService)
        {
            this.sharedDbContext = sharedDbContext;
            this.tenantDbContextFactory = tenantDbContextFactory;
            this._emailService = _emailService;
        }

        public async Task<CenterResponseDto> CreateCenterAsync(createCenterRequestDto request)
        {
            var plan = await sharedDbContext.SubscriptionPlans.FindAsync(request.PlanId);
            if (plan == null) throw new BusinessException("Subscription.PlanNotFound");


            var center = new MedicalCenter
            {
                Name = request.Name,
                Phone = request.Phone,
                Address = request.Address,
                RequiresPrepayment = request.RequiresPrepayment,
                PrepaymentAmount = request.PrepaymentAmount,
                RefundPolicyHours = request.RefundPolicyHours,
                PartialRefundPercent = request.PartialRefundPercent,
                CenterStatus = CenterStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };
            if (request.AdminWhatsappNumber != null)
            {
                center.AdminWhatsappNumber = request.AdminWhatsappNumber;
            }
            if (request.AddedBySuperAdminId != null)
            {
                center.AddedBySuperAdminId = request.AddedBySuperAdminId;
            }

            if (request.AdminEmail != null)
            {
                center.AdminEmail = request.AdminEmail;
            }
            if (request.Logo != null)
            {

                var fileName = Guid.NewGuid() + Path.GetExtension(request.Logo.FileName);

                var path = Path.Combine("wwwroot/uploads/receipts", fileName);

                using (var stream = new FileStream(path, FileMode.Create))
                {
                    await request.Logo.CopyToAsync(stream);
                }
                center.LogoUrl = $"/uploads/receipts/{fileName}";
            }
            // name of domain
            var centerUrl = $"{GenerateSlug(request.Name)}.sehatak.com";
            var urlExists = await sharedDbContext.MedicalCenters
              .AnyAsync(c => c.UniqueUrl == centerUrl);
            if (urlExists)
                throw new BusinessException("Center.UniqueUrlExists");

            center.UniqueUrl = centerUrl;

            await sharedDbContext.MedicalCenters.AddAsync(center);
            await sharedDbContext.SaveChangesAsync();

            var subscription = new CenterSubscription
            {
                CenterId = center.Id,
                PlanId = plan.Id,
                StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
                EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(plan.DurationDays)),
                Status = SubscriptionStatus.Active,
                AmountPaid = plan.Price
            };

            await sharedDbContext.CenterSubscriptions.AddAsync(subscription);


            var planFeatures = await sharedDbContext.PlanFeatures.Where(p => p.PlanId == request.PlanId).ToListAsync();
            foreach (var pf in planFeatures)
            {
                await sharedDbContext.CenterFeatures.AddAsync(new CenterFeature
                {
                    CenterId = center.Id,
                    FeatureId = pf.FeatureId,
                    IsEnabled = true
                });
            }

            await tenantDbContextFactory.CreateTenantDatabaseAsync(center.Id);

            center.CenterStatus = CenterStatus.Active;
            await sharedDbContext.SaveChangesAsync();

            var enabledFeatureNames = await sharedDbContext.CenterFeatures
                .Where(c => c.CenterId == center.Id)
                .Include(c => c.Feature)
                .Select(c => c.Feature.NameOfFeature)
                .ToListAsync();

            return new CenterResponseDto
            {
                Id = center.Id,
                Name = center.Name,
                UniqueUrl = center.UniqueUrl,
                Status = center.CenterStatus.ToString(),
                EnabledFeatures = enabledFeatureNames
            };

        }
        public string GenerateSlug(string name)
        {
            return name
                  .Trim()
                  .ToLower()
                  .Replace(" ", "-");
        }

        public async Task<CreateAdminResponseDto> CreateAdminAsync(int centerId, CreateAdminRequestDto request)
        {
            var center = await sharedDbContext.MedicalCenters
                .FindAsync(centerId);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = tenantDbContextFactory.CreateForCenter(centerId);

            var emailExists = await db.Users.AnyAsync(u => u.email == request.Email);
            if (emailExists)
                throw new BusinessException("Auth.EmailExists");

            var tempPassword = GenerateTempPassword();

            var admin = new User
            {
                firstName = request.FirstName,
                lastName = request.LastName,
                address = request.Address,
                city = request.City,
                phoneNumber = request.PhoneNumber,
                email = request.Email,
                passwordHash = BCrypt.Net.BCrypt.HashPassword(tempPassword),
                role = userRole.Admin,
                isActive = true,
                createdAt = DateTime.UtcNow
            };
            await db.Users.AddAsync(admin);
            await db.SaveChangesAsync();

            await _emailService.SendTempPasswordAsync(
                request.Email, name: $"{request.FirstName} {request.LastName}", tempPassword, center.Name
                );

            return new CreateAdminResponseDto
            {
                UserId = admin.Id,
                FullName = $"{admin.firstName} {admin.lastName}",
                Email = admin.email!,
                Message = "تم إنشاء حساب الأدمن وإرسال كلمة السر المؤقتة بالإيميل."
            };
        }
        private string GenerateTempPassword()
        {
            const string chars = "ABCDEFGHJKMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789";
            var random = new Random();
            return new string(Enumerable.Repeat(chars, 10)
                .Select(s => s[random.Next(s.Length)])
                .ToArray());
        }
        public async Task<SpasificCenterResponseDto> GetSpasificCenterById(int centerId)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId);
            if (center == null)
                throw new BusinessException("Center.NotFound");

            var subscription = await sharedDbContext.CenterSubscriptions
                .Include(c => c.Plan)
                .FirstOrDefaultAsync(c => c.CenterId == centerId && c.Status == SubscriptionStatus.Active);

            if (subscription == null)
                throw new BusinessException("Center.SubscriptionExpired");

            var features = await sharedDbContext.PlanFeatures
                .Where(p => p.PlanId == subscription.PlanId)
                .Select(f => f.Feature.NameOfFeature)
                .ToListAsync();

            return new SpasificCenterResponseDto
            {
                Id = center.Id,
                Name = center.Name,
                UniqueUrl = center.UniqueUrl,
                Phone = center.Phone,
                Address = center.Address,
                LogoUrl = center.LogoUrl,
                AddedBySuperAdminId = center.AddedBySuperAdminId,
                AdminWhatsappNumber = center.AdminWhatsappNumber,
                PlanName = subscription.Plan.Name,
                FeatureNames = features
            };

        }
        public async Task<List<ListOfCentersResponse>> GetListOfCenters()
        {
            var centers = await sharedDbContext.MedicalCenters
                .Select(c => new ListOfCentersResponse
                {
                    Id = c.Id,
                    Name = c.Name,
                    UniqueUrl = c.UniqueUrl,
                    Phone = c.Phone,
                    Address = c.Address,
                    LogoUrl = c.LogoUrl,
                    AddedBySuperAdminId = c.AddedBySuperAdminId,
                    AdminWhatsappNumber = c.AdminWhatsappNumber

                })
                .ToListAsync();

            return centers;

        }
        public async Task<bool> SuspendedCenter(int centerId)
        {
            var center = await sharedDbContext.MedicalCenters.FirstOrDefaultAsync(c => c.Id == centerId
            && c.CenterStatus == CenterStatus.Active);
            if (center == null)
                throw new BusinessException("Center.NotFound");

            var subscription = await sharedDbContext.CenterSubscriptions
                .FirstOrDefaultAsync(s => s.Center.Id == centerId
                && s.Status == SubscriptionStatus.Active);

            if (subscription == null)
                throw new BusinessException("Subscription.PlanNotFound");


            center.CenterStatus = CenterStatus.Suspended;
            subscription.Status = SubscriptionStatus.Cancelled;

            await sharedDbContext.SaveChangesAsync();
            return true;

        }
        public async Task<bool> ActiveCenter(int centerId)
        {
            var center = await sharedDbContext.MedicalCenters.FirstOrDefaultAsync(c => c.Id == centerId
            && c.CenterStatus == CenterStatus.Suspended);
            if (center == null)
                throw new BusinessException("Center.NotFound");

            var subscription = await sharedDbContext.CenterSubscriptions
                .FirstOrDefaultAsync(s => s.Center.Id == centerId
                && s.Status == SubscriptionStatus.Cancelled);

            if (subscription == null)
                throw new BusinessException("Subscription.PlanNotFound");

            if (subscription.EndDate < DateOnly.FromDateTime(DateTime.UtcNow))
                throw new BusinessException("Center.SubscriptionExpired");

            center.CenterStatus = CenterStatus.Active;
            subscription.Status = SubscriptionStatus.Active;
            await sharedDbContext.SaveChangesAsync();

            return true;
        }
    }
}
