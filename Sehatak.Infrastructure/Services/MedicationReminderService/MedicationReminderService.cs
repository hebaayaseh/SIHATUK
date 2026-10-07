
using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.DTOs.MedicationReminderDto;
using Sehatak.Application.Interfaces.IMedicationReminder;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;

namespace Sehatak.Infrastructure.Services.MedicationReminderService
{
    public class MedicationReminderService : IMedicationReminder
    {
        private readonly SharedDbContext sharedDbContext;
        private readonly TenantDbContextFactory contextFactory;
        public MedicationReminderService(SharedDbContext sharedDbContext , TenantDbContextFactory contextFactory)
        {
            this.sharedDbContext = sharedDbContext;
            this.contextFactory = contextFactory;
        }

        public async Task<MedicationReminderResponseDto> AddMedicationReminderAsync(int centerId, int userId, MedicationReminderRequestDto request, int? subPatientId)
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
                throw new BusinessException("Center.NotFound");

            Patient actingPatient = patient;

            if (subPatientId.HasValue)
            {
                var subPatient = await db.Patients
                    .FirstOrDefaultAsync(s => s.ParentPatientId == actingPatient.patientId
                                        && s.patientId == subPatientId);

                if (subPatient == null)
                    throw new BusinessException("SubPatient.NotFound");

                actingPatient = subPatient;
            }

            var startDate = DateOnly.FromDateTime(DateTime.UtcNow);

            var reminder = new MedicationReminder
            {
                PatientId = actingPatient.patientId,
                MedicationName = request.MedicationName,
                ReminderTime = request.ReminderTime,
                Days = request.IsDaily ? ReminderDays.All : request.SelectedDays,
                StartDate = startDate,
                EndDate = startDate.AddDays(request.DurationInDays),
                IsActive = true
            };

            await db.MedicationReminders.AddAsync(reminder);
            await db.SaveChangesAsync();

            return new MedicationReminderResponseDto
            {
                Id = reminder.Id,
                MedicationName = reminder.MedicationName,
                ReminderTime = reminder.ReminderTime,
                Days = reminder.Days,
                StartDate = reminder.StartDate,
                EndDate = reminder.EndDate,
                IsActive = reminder.IsActive,
                CreatedAt = reminder.CreatedAt,
                PatientId = actingPatient.patientId,
                PatientName = actingPatient.userId != null
                    ? $"{actingPatient.user.firstName} {actingPatient.user.lastName}"
                    : $"{actingPatient.FirstName} {actingPatient.LastName}"
            };
        }


        public async Task<PagedResult<MedicationReminderResponseDto>> GetMedicationRemindersAsync(int centerId, int userId, PagedRequest request, int? subPatientId)
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

            Patient actingPatient = patient;

            if (subPatientId.HasValue)
            {
                var subPatient = await db.Patients
                    .FirstOrDefaultAsync(s => s.patientId == subPatientId.Value
                                         && s.ParentPatientId == patient.patientId);

                if (subPatient == null)
                    throw new BusinessException("SubPatient.NotFoundOrNotOwned");

                actingPatient = subPatient;
            }

            var query = db.MedicationReminders
                .Where(r => r.PatientId == actingPatient.patientId)
                .OrderByDescending(r => r.IsActive)
                .ThenBy(r => r.ReminderTime)
                .Select(r => new MedicationReminderResponseDto
                {
                    Id = r.Id,
                    MedicationName = r.MedicationName,
                    ReminderTime = r.ReminderTime,
                    Days = r.Days,
                    StartDate = r.StartDate,
                    EndDate = r.EndDate,
                    IsActive = r.IsActive,
                    CreatedAt = r.CreatedAt,
                    PatientId = actingPatient.patientId,
                    PatientName = actingPatient.userId != null
                    ? $"{actingPatient.user.firstName} {actingPatient.user.lastName}"
                    : $"{actingPatient.FirstName} {actingPatient.LastName}"
                });

            return await query.ToPagedResultAsync(request.PageNumber, request.PageSize);
        }

        public async Task<string> DeleteMedicationReminderAsync(int centerId, int userId, int reminderId, int? subPatientId)
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

            Patient actingPatient = patient;

            if (subPatientId.HasValue)
            {
                var subPatient = await db.Patients
                    .FirstOrDefaultAsync(s => s.patientId == subPatientId.Value
                                         && s.ParentPatientId == patient.patientId);

                if (subPatient == null)
                    throw new BusinessException("SubPatient.NotFoundOrNotOwned");

                actingPatient = subPatient;
            }

            var reminder = await db.MedicationReminders
                .FirstOrDefaultAsync(r => r.Id == reminderId
                                     && r.PatientId == actingPatient.patientId);

            if (reminder == null)
                throw new BusinessException("MedicationReminder.NotFound");

            db.MedicationReminders.Remove(reminder);
            await db.SaveChangesAsync();

            return "تم حذف التذكير بنجاح.";
        }
    }
}