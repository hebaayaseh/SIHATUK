using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.DTOs.FollowUpDto;
using Sehatak.Application.Interfaces.IFollowUp;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;

namespace Sehatak.Infrastructure.Services.FollowUpService
{
    public class FollowUpService : IFollowUp
    {
        private readonly SharedDbContext sharedDbContext;
        private readonly TenantDbContextFactory contextFactory;
        public FollowUpService(SharedDbContext sharedDbContext, TenantDbContextFactory contextFactory)
        {
            this.sharedDbContext = sharedDbContext;
            this.contextFactory = contextFactory;
        }
        public async Task<FollowUpResponseDto> DoctorAddFollowUpAsync(int centerId, int userId, DoctorAddFollowUpRequestDto request)
        {
            var today = ClinicClock.Today;

            if (request.AllowFollowUpDate < today && request.AllowFollowUpDate!=null)
                throw new BusinessException("Date.Invalid");

            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NoFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var doctor = await db.Doctors
                .Include(u => u.user)
                .FirstOrDefaultAsync(d => d.userId == userId
                                     && d.user.isActive);

            if (doctor == null)
                throw new BusinessException("Doctor.NoFound");

            var appointmentExists = await db.Appointments
                .AnyAsync(a => a.Id == request.OriginalAppointmentId
                           && a.doctorId == doctor.Id
                           && a.appointmentStatus == AppointmentStatus.Confirmed
                           && a.patientId == request.PatientId);

            if (!appointmentExists)
                throw new BusinessException("Appointment.NoFound");

            var duplicateFollowUp = await db.FollowUps
                .AnyAsync(f => f.OriginalAppointmentId == request.OriginalAppointmentId
                           && f.Status == FollowUpStatus.Pending);

            if (duplicateFollowUp)
                throw new BusinessException("FollowUp.AlreadyExists");

            var now = DateTime.UtcNow;
            var followUp = new FollowUp
            {
                OriginalAppointmentId = request.OriginalAppointmentId,
                PatientId = request.PatientId,
                DoctorId = doctor.Id,
                AllowFollowUpDate = request.AllowFollowUpDate,
                Status = FollowUpStatus.Pending,
                CreatedAt = now,
                UpdatedAt = now,
            };
            await db.FollowUps.AddAsync(followUp);

            var patientInfo = await db.Patients
                .Where(p => p.patientId == request.PatientId)
                .Select(p => new
                {
                    PatientName = p.userId != null
                        ? p.user.firstName + " " + p.user.lastName
                        : p.FirstName + " " + p.LastName,
                    p.NotifiableUserId,
                })
                .FirstAsync();

            var notifiableUserExists = await db.Users
                .AnyAsync(u => u.Id == patientInfo.NotifiableUserId);

            if (notifiableUserExists)
            {
                await db.Notifications.AddAsync(new Notification
                {
                    UserId = patientInfo.NotifiableUserId,
                    Message = $"تم تحديد موعد للمتابعة بتاريخ {request.AllowFollowUpDate}.",
                    Type = NotificationType.Appointment,
                    CreatedAt = now,
                });
            }

            await db.SaveChangesAsync();
            return new FollowUpResponseDto
            {
                Id = followUp.Id,
                OriginalAppointmentId = followUp.OriginalAppointmentId,
                PatientId = followUp.PatientId,
                PatientName = patientInfo.PatientName,
                DoctorId = followUp.DoctorId,
                DoctorName = doctor.user.firstName + " " + doctor.user.lastName,
                AllowFollowUpDate = followUp.AllowFollowUpDate,
                Status = followUp.Status,
                CreatedAt = followUp.CreatedAt,
                UpdatedAt = followUp.UpdatedAt,
            };

        }

        public async Task<PagedResult<DoctorGetAllFollowUpResponse>> DoctorGetAllFollowUpAsync(int centerId, int userId, PagedRequest request)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NoFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var doctor = await db.Doctors
                .Include(u => u.user)
                .FirstOrDefaultAsync(d => d.userId == userId
                                     && d.user.isActive);

            if (doctor == null)
                throw new BusinessException("Doctor.NoFound");

            var query = db.FollowUps
                .Where(f => f.DoctorId == doctor.Id)
                .OrderByDescending(f => f.CreatedAt)
                .Select(f => new DoctorGetAllFollowUpResponse
                {
                    FollowUpId = f.Id,
                    PatientId = f.PatientId,
                    PatientName = f.Patient.userId != null
                    ? f.Patient.user.firstName + " " + f.Patient.user.lastName
                    : f.Patient.FirstName + " " + f.Patient.LastName,
                    AllowFollowUpDate = f.AllowFollowUpDate,
                    followUpStatus = f.Status,
                    CreatedAt = f.CreatedAt,
                    UpdatedAt = f.UpdatedAt,
                });
            return await query.ToPagedResultAsync(request.PageNumber, request.PageSize);
        }

        public async Task<FollowUpResponseDto> DoctorUpdateFollowUpAsync(int centerId, int userId, UpdateFollowUpRequestDto request)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NoFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var doctor = await db.Doctors
                .Include(u => u.user)
                .FirstOrDefaultAsync(d => d.userId == userId
                                     && d.user.isActive);

            if (doctor == null)
                throw new BusinessException("Doctor.NoFound");

            var followUp = await db.FollowUps
                .FirstOrDefaultAsync(f => f.Id == request.FollowUpId
                                     && f.DoctorId == doctor.Id
                                     && f.PatientId == request.PatientId);

            if (followUp == null)
                throw new BusinessException("FollowUp.NoFound");

            if (followUp.Status != FollowUpStatus.Pending)
                throw new BusinessException("FollowUp.CannotBeUpdated");

            var patientInfo = await db.Patients
                .Where(p => p.patientId == followUp.PatientId)
                .Select(p => new
                {
                    PatientName = p.userId != null
                        ? p.user.firstName + " " + p.user.lastName
                        : p.FirstName + " " + p.LastName,
                    p.NotifiableUserId,
                })
                .FirstAsync();

            var now = ClinicClock.Now;
            followUp.AllowFollowUpDate = request.AllowFollowUpDate;
            followUp.UpdatedAt = now;

            var notifiableUserExists = await db.Users
                .AnyAsync(u => u.Id == patientInfo.NotifiableUserId);

            if (notifiableUserExists)
            {
                await db.Notifications.AddAsync(new Notification
                {
                    UserId = patientInfo.NotifiableUserId,
                    Message = $"تم تعديل موعد المتابعة إلى {request.AllowFollowUpDate}.",
                    CreatedAt = now,
                    Type = NotificationType.Appointment,
                });
            }

            await db.SaveChangesAsync();

            return new FollowUpResponseDto
            {
                Id = followUp.Id,
                OriginalAppointmentId = followUp.OriginalAppointmentId,
                PatientId = followUp.PatientId,
                PatientName = patientInfo.PatientName,
                DoctorId = followUp.DoctorId,
                DoctorName = doctor.user.firstName + " " + doctor.user.lastName,
                AllowFollowUpDate = followUp.AllowFollowUpDate,
                Status = followUp.Status,
                CreatedAt = followUp.CreatedAt,
                UpdatedAt = followUp.UpdatedAt,
            };
        }

        public async Task<PagedResult<PatientGetAllFollowUpResponseDto>> PatientGetAllFollowUpAsync(int centerId, int userId, PagedRequest request, int? subPatientId)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NoFound");

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

            var query = db.FollowUps
                .Where(f => f.PatientId == actingPatient.patientId)
                .OrderByDescending(f => f.CreatedAt)
                .Select(f => new PatientGetAllFollowUpResponseDto
                {
                    Id = f.Id,
                    DoctorId = f.DoctorId,
                    DoctorName = f.Doctor.user.firstName + " " + f.Doctor.user.lastName,
                    AllowFollowUpDate = f.AllowFollowUpDate,
                    Status = f.Status,
                    CreatedAt = f.CreatedAt,
                    UpdatedAt = f.UpdatedAt,
                });
            return await query.ToPagedResultAsync(request.PageNumber, request.PageSize);
        }

        public async Task<FollowUpResponseDto> ReceptionistAddFollowUpAsync(int centerId, int userId, ReceptionistAddFollowUpRequestDto request)
        {
            var today = ClinicClock.Today;
            if (request.AllowFollowUpDate < today && request.AllowFollowUpDate != null)
                throw new BusinessException("Date.Invalid");

            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NoFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var receptionist = await db.Users
                .FirstOrDefaultAsync(d => d.Id == userId
                                     && d.isActive);

            if (receptionist == null)
                throw new BusinessException("Receptionist.NoFound");

            var appointment = await db.Appointments
              .Include(a => a.Patient)
              .ThenInclude(p => p.user)
              .FirstOrDefaultAsync(a => a.Id == request.OriginalAppointmentId
                         && a.appointmentStatus == AppointmentStatus.Confirmed
                         && a.patientId == request.PatientId
                         && a.doctorId == request.DoctorId);

            if (appointment == null)
                throw new BusinessException("Appointment.NoFound");

            var doctor = await db.Doctors
                .Include(d => d.user)
                .FirstOrDefaultAsync(d => d.Id == request.DoctorId && d.user.isActive);

            if (doctor == null)
                throw new BusinessException("Doctor.NoFound");

            var duplicateFollowUp = await db.FollowUps
                .AnyAsync(f => f.OriginalAppointmentId == request.OriginalAppointmentId
                           && f.Status == FollowUpStatus.Pending);

            if (duplicateFollowUp)
                throw new BusinessException("FollowUp.AlreadyExists");

            var now = ClinicClock.Now;
            var followUp = new FollowUp
            {
                OriginalAppointmentId = request.OriginalAppointmentId,
                PatientId = request.PatientId,
                DoctorId = request.DoctorId,
                ReceptionistId = receptionist.Id,  
                AllowFollowUpDate = request.AllowFollowUpDate,
                Status = FollowUpStatus.Pending,
                CreatedAt = now,
                UpdatedAt = now,
            };
            await db.FollowUps.AddAsync(followUp);

            var notifiableUserExists = await db.Users
                .AnyAsync(u => u.Id == appointment.Patient.NotifiableUserId);

            if (notifiableUserExists)
            {
                await db.Notifications.AddAsync(new Notification
                {
                    UserId = appointment.Patient.NotifiableUserId,
                    Message = $"تم تحديد موعد للمتابعة بتاريخ {request.AllowFollowUpDate}.",
                    Type = NotificationType.Appointment,
                    CreatedAt = now,
                });
            }

            await db.SaveChangesAsync();

            return new FollowUpResponseDto
            {
                Id = followUp.Id,
                OriginalAppointmentId = followUp.OriginalAppointmentId,
                PatientId = followUp.PatientId,
                PatientName = appointment.Patient.userId != null
                    ? appointment.Patient.user.firstName + " " + appointment.Patient.user.lastName
                    : appointment.Patient.FirstName + " " + appointment.Patient.LastName,
                DoctorId = followUp.DoctorId,
                DoctorName = doctor.user.firstName + " " + doctor.user.lastName,
                AllowFollowUpDate = followUp.AllowFollowUpDate,
                Status = followUp.Status,
                CreatedAt = followUp.CreatedAt,
                UpdatedAt = followUp.UpdatedAt,
            };
        }

        public async Task<PagedResult<ReceptionistGetAllFollowUpResponseDto>> ReceptionistGetAllFollowUpAsync(int centerId, int userId, PagedRequest request)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NoFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var receptionist = await db.Users
                .FirstOrDefaultAsync(d => d.Id == userId
                                     && d.isActive);

            if (receptionist == null)
                throw new BusinessException("Receptionist.NoFound");

            var query = db.FollowUps
              .Where(f => f.Status == FollowUpStatus.Pending)
              .GroupBy(f => f.DoctorId)
              .OrderByDescending(g => g.Max(f => f.CreatedAt))
              .Select(g => new ReceptionistGetAllFollowUpResponseDto
              {
                  DoctorId = g.Key,
                  DoctorName = g.First().Doctor.user.firstName + " " + g.First().Doctor.user.lastName,
                  FollowUps = g.Select(f => new DoctorGetAllFollowUpResponse
                  {
                       FollowUpId = f.Id,
                       PatientId = f.PatientId,
                       PatientName = f.Patient.userId != null
                         ? f.Patient.user.firstName + " " + f.Patient.user.lastName
                         : f.Patient.FirstName + " " + f.Patient.LastName,
                       AllowFollowUpDate = f.AllowFollowUpDate,
                       followUpStatus = f.Status,
                       CreatedAt = f.CreatedAt,
                       UpdatedAt = f.UpdatedAt,
                  }).ToList()
              });

            return await query.ToPagedResultAsync(request.PageNumber, request.PageSize);

        }

        public async Task<FollowUpResponseDto> ReceptionistUpdateFollowUpAsync(int centerId, int userId, UpdateFollowUpRequestDto request)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NoFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var receptionist = await db.Users
               .FirstOrDefaultAsync(d => d.Id == userId && d.isActive);

            if (receptionist == null)
                throw new BusinessException("Receptionist.NoFound");


            var followUp = await db.FollowUps
                .FirstOrDefaultAsync(f => f.Id == request.FollowUpId
                                     && f.PatientId == request.PatientId);

            if (followUp == null)
                throw new BusinessException("FollowUp.NoFound");

            if (followUp.Status != FollowUpStatus.Pending)
                throw new BusinessException("FollowUp.CannotBeUpdated");

            var info = await db.FollowUps
                .Where(f => f.Id == followUp.Id)
                .Select(f => new
                {
                    PatientName = f.Patient.userId != null
                        ? f.Patient.user.firstName + " " + f.Patient.user.lastName
                        : f.Patient.FirstName + " " + f.Patient.LastName,
                    f.Patient.NotifiableUserId,
                    DoctorName = f.Doctor.user.firstName + " " + f.Doctor.user.lastName,
                })
                .FirstAsync();

            followUp.AllowFollowUpDate = request.AllowFollowUpDate;
            followUp.UpdatedAt = DateTime.UtcNow;

            var notifiableUserExists = await db.Users.AnyAsync(u => u.Id == info.NotifiableUserId);

            if (notifiableUserExists)
            {
                await db.Notifications.AddAsync(new Notification
                {
                    UserId = info.NotifiableUserId,
                    Message = $"تم تعديل موعد المتابعة إلى {request.AllowFollowUpDate}.",
                    CreatedAt = DateTime.UtcNow,
                    Type = NotificationType.Appointment,
                });
            }

            await db.SaveChangesAsync();

            return new FollowUpResponseDto
            {
                Id = followUp.Id,
                OriginalAppointmentId = followUp.OriginalAppointmentId,
                PatientId = followUp.PatientId,
                PatientName = info.PatientName,
                DoctorId = followUp.DoctorId,
                DoctorName = info.DoctorName,
                AllowFollowUpDate = followUp.AllowFollowUpDate,
                Status = followUp.Status,
                CreatedAt = followUp.CreatedAt,
                UpdatedAt = followUp.UpdatedAt,
            };

        }
    }
}
