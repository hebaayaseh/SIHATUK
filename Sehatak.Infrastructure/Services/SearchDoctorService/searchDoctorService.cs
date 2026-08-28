using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.DTOs.GetStaffDto;
using Sehatak.Application.DTOs.SearchDoctorDto;
using Sehatak.Application.Interfaces.SearchDoctor;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;
using Sehatak.Infrastructure.Services.GetStaff;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Infrastructure.Services.SearchDoctorService
{
    public class searchDoctorService : ISearchDoctor
    {
        private readonly SharedDbContext sharedDbContext;
        private readonly TenantDbContextFactory contextFactory;
        public searchDoctorService(SharedDbContext sharedDbContext, TenantDbContextFactory contextFactory)
        {
            this.sharedDbContext = sharedDbContext;
            this.contextFactory = contextFactory;
        }

        public async Task<List<DoctorSummaryDto>> SearchDoctorAsync(int centerId, SearchDoctorRequest request)
        {
            var center = await sharedDbContext.MedicalCenters
                 .FirstOrDefaultAsync(c => c.Id == centerId && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Cente.NotFound");
            using var db = contextFactory.CreateForCenter(centerId);
            var query = db.Doctors
                .Include(d => d.user)
                .Where(d => d.user.isActive)
                .AsQueryable();


            if (!string.IsNullOrWhiteSpace(request.doctorName))
            {
                var search = request.doctorName.ToLower().Trim();
                query = query.Where(d => d.user.firstName.ToLower().Contains(search)
                           || d.user.lastName.ToLower().Contains(search)
                           || (d.user.firstName + " " + d.user.lastName).ToLower().Contains(search));
            }
            if (request.departmentId != null)
                query = query.Where(d => d.departmentId == request.departmentId);

            if (!string.IsNullOrWhiteSpace(request.Specialization))
            {
                var search = request.Specialization.ToLower().Trim();
                query = query.Where(s => s.Specialization.ToLower().Trim().Contains(search));
            }

            var result = await query.Select(a => new DoctorSummaryDto
            {
                DoctorId = a.Id,
                DoctorName = a.user.firstName + " " + a.user.lastName,
                Specialization = a.Specialization,
                ProfileImageUrl = a.user.ProfileImageUrl,
                OnlineEnabled = a.OnlineEnabled,
                Bio = a.Bio,
                doctorSchedule = a.doctorschedules
                .Where(s => s.IsActive)
                .Select(d => new SummarySchedualDto
                {
                    Id = d.Id,
                    StartTime = d.StartTime,
                    EndTime = d.EndTime,
                    SlotDurationMinutes = d.SlotDurationMinutes,
                    IsActive = d.IsActive,
                }).ToList(),

            }).ToListAsync();

            return result;
        }
    }
}
