

using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.DTOs.ViewProfileDto;
using Sehatak.Application.Interfaces.IViewProfile;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;

namespace Sehatak.Infrastructure.Services.ViewProfileService
{
    public class ViewProfileService : IViewProfile
    {
        private readonly SharedDbContext sharedDbContext;
        private readonly TenantDbContextFactory contextFactory;
        public ViewProfileService(SharedDbContext sharedDbContext , TenantDbContextFactory contextFactory)
        {
            this.sharedDbContext = sharedDbContext;
            this.contextFactory = contextFactory;
        }

        public async Task<string> PatientDeactiveProfileAsync(int centerId, int userId)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var user = await db.Users
                .Include(p => p.patient)
                .FirstOrDefaultAsync(p => p.Id == userId
                                     && p.isActive);

            if (user == null)
                throw new BusinessException("User.NotFound");

            user.isActive = false;
            await db.SaveChangesAsync();
            return "تم تعطيل الحساب بنجاح.";
        }

        public async Task<ViewPatientProfileResponseDto> ViewPatientProfileAsync(int centerId, int userId)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c=>c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var user = await db.Users
                .Include(p=>p.patient)
                .FirstOrDefaultAsync(p=>p.Id == userId
                                     && p.isActive);

            if (user == null)
                throw new BusinessException("User.NotFound");

            return new ViewPatientProfileResponseDto
            {
                Id = userId,
                FirstName = user.firstName,
                LastName = user.lastName,
                PhoneNumber = user.phoneNumber,
                City = user.city,
                Adreess = user.address,
                ProfileImage = user.ProfileImageUrl,
                BloodType = user.patient.BloodType,
                Gender = user.patient.Gender,
                DateOfBith = user.patient.DateOfBith,
                Email = user.email
            };
        }
    }
}
