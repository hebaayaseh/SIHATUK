using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.DTOs.SubPatientDto;
using Sehatak.Application.Interfaces.ISubPatient;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Infrastructure.Services.SupPatientService
{
    public class SubPatientService : ISubPatient
    {
        private readonly SharedDbContext sharedDbContext;
        private readonly TenantDbContextFactory contextFactory;
        public SubPatientService(SharedDbContext sharedDbContext, TenantDbContextFactory contextFactory)
        {
            this.sharedDbContext = sharedDbContext;
            this.contextFactory = contextFactory;
        }
        public async Task<List<SummarySubPatientResponseDto>> AddSubPatientAsync(int centerId, int userId, AddSubPatientRequestDto request)
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

            if (!request.SubPatients.Any())
                return new List<SummarySubPatientResponseDto>();


            var newSubPatients = request.SubPatients.Select(sp => new Patient
            {
                FirstName = sp.SubPatientFirstName,
                LastName = sp.SubPatientLastName,
                DateOfBith = sp.DateOfBith,
                WhatsappNumber = sp.WhatAppNumber,
                BloodType = sp.BloodType,
                Gender = sp.Gender,
                ParentPatientId = patient.patientId
            }).ToList();

            await db.Patients.AddRangeAsync(newSubPatients);

            await db.Notifications.AddAsync(new Notification
            {
                Message = "تم إضافة تابع جديد لحسابك بنجاح.",
                UserId = userId,
                CreatedAt = DateTime.UtcNow,
                Type = NotificationType.System,
                IsRead = false
            });

            await db.SaveChangesAsync();
            

            return newSubPatients.Select(sp => new SummarySubPatientResponseDto
            {
                Id = sp.patientId,
                SubPatientFirstName = sp.FirstName,
                SubPatientLastName = sp.LastName,
                DateOfBith = sp.DateOfBith,
                WhatAppNumber = sp.WhatsappNumber,
                BloodType = sp.BloodType,
                Gender = sp.Gender
            }).ToList();

        }

        
    }
}
