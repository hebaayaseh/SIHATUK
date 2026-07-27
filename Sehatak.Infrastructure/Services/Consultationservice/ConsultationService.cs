using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.ConsultationDto;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.Interfaces.ConsultaionInterface;
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
    }
}
