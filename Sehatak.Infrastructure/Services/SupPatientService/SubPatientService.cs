using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.DTOs.SubPatientDto;
using Sehatak.Application.Interfaces.AuditLog;
using Sehatak.Application.Interfaces.ISubPatient;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;

namespace Sehatak.Infrastructure.Services.SupPatientService
{
    public class SubPatientService : ISubPatient
    {
        private readonly SharedDbContext sharedDbContext;
        private readonly TenantDbContextFactory contextFactory;
        private readonly IAuditLog auditLog;
        public SubPatientService(SharedDbContext sharedDbContext, TenantDbContextFactory contextFactory, IAuditLog auditLog)
        {
            this.sharedDbContext = sharedDbContext;
            this.contextFactory = contextFactory;
            this.auditLog = auditLog;
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
                ParentPatientId = patient.patientId,
                NotifiableUserId = patient.userId!.Value,
            }).ToList();
            using var transaction = await db.Database.BeginTransactionAsync();
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

            var auditEntry = auditLog.Build(
                action: "AddSubPatient",
                entityType: "Patient",
                entityId: patient.patientId,
                newValue: new
                {
                    patientId = patient.patientId,
                    SubPatients = newSubPatients.Select(sp => new
                    {
                        sp.FirstName,
                        sp.LastName,
                        sp.DateOfBith,
                        sp.WhatsappNumber,
                        sp.BloodType,
                        sp.Gender
                    }).ToList()
                });
            if (auditEntry != null)
                db.AuditLogs.Add(auditEntry);

            await db.SaveChangesAsync();
            await transaction.CommitAsync();

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


        public async Task<PagedResult<SummarySubPatientResponseDto>> GetSubPatientsAsync(int centerId, int userId, PagedRequest request)
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

            var query = db.Patients
                .Where(s => s.ParentPatientId == patient.patientId)
                .OrderBy(n => n.FirstName)
                .ThenBy(n => n.LastName)
                .Select(s=>new SummarySubPatientResponseDto
                {
                    Id = s.patientId,
                    SubPatientFirstName = s.FirstName,
                    SubPatientLastName = s.LastName,
                    DateOfBith = s.DateOfBith,
                    WhatAppNumber = s.WhatsappNumber,
                    BloodType = s.BloodType,
                    Gender = s.Gender
                });
            return await query.ToPagedResultAsync(request.PageNumber, request.PageSize);
        }

        public async Task<SummarySubPatientResponseDto> UpdateSubPatientAsync(int centerId, int userId, int subPatientId, UpdateSubPatientRequestDto request)
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

            var subPatient = await db.Patients
                .FirstOrDefaultAsync(s => s.ParentPatientId == patient.patientId
                                     && s.patientId == subPatientId);

            if (subPatient == null)
                throw new BusinessException("SubPatient.NotFound");

            var oldValue = new
            {
                subPatientId = subPatient.patientId,
                SubPatientFirstName = subPatient.FirstName,
                SubPatientLastName = subPatient.LastName,
                DateOfBith = subPatient.DateOfBith,
                Gender = subPatient.Gender,
                BloodType = subPatient.BloodType,
                WhatAppNumber = subPatient.WhatsappNumber
            };


            if (request.SubPatientFirstName != null)
                subPatient.FirstName = request.SubPatientFirstName;

            if (request.SubPatientLastName != null)
                subPatient.LastName = request.SubPatientLastName;

            if (request.DateOfBith != null)
                subPatient.DateOfBith = request.DateOfBith.Value;

            if (request.Gender != null)
                subPatient.Gender = (Gender)request.Gender;

            if (request.WhatAppNumber != null)
                subPatient.WhatsappNumber = request.WhatAppNumber;

            if (request.BloodType != null)
                subPatient.BloodType = (BloodType)request.BloodType;

            var auditEntry = auditLog.Build(
                action: "UpdateSubPatient",
                entityType: "Patient",
                entityId: subPatient.patientId,
                oldValue: oldValue,
                newValue: new
                {
                    subPatientId = subPatient.patientId,
                    SubPatientFirstName = subPatient.FirstName,
                    SubPatientLastName = subPatient.LastName,
                    DateOfBith = subPatient.DateOfBith,
                    Gender = subPatient.Gender,
                    BloodType = subPatient.BloodType,
                    WhatAppNumber = subPatient.WhatsappNumber
                });

            if (auditEntry != null)
                db.AuditLogs.Add(auditEntry);

            await db.SaveChangesAsync();

            return new SummarySubPatientResponseDto
            {
                Id = subPatient.patientId,
                SubPatientFirstName = subPatient.FirstName,
                SubPatientLastName = subPatient.LastName,
                DateOfBith = subPatient.DateOfBith,
                Gender = subPatient.Gender,
                BloodType = subPatient.BloodType,
                WhatAppNumber = subPatient.WhatsappNumber
            };

        }
    }
}
