using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.DTOs.MedicalRecordDto;
using Sehatak.Application.Interfaces.IMedicalRecord;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.PaymentEnums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;
using System.Linq.Dynamic.Core;

namespace Sehatak.Infrastructure.Services.MedicalRecordService
{
    public class MedicalRecordService : IMedicalRecord
    {
        private readonly SharedDbContext sharedDbContext;
        private readonly TenantDbContextFactory contextFactory;
        public MedicalRecordService(SharedDbContext sharedDbContext, TenantDbContextFactory contextFactory)
        {
            this.sharedDbContext = sharedDbContext;
            this.contextFactory = contextFactory;
        }

        public async Task<MedicalRecordDetailResponseDto> AddMedicalRecordAsync(int centerId, int userId, MedicalRecordDetailRequestDto request)
        {
            if (request.AppointmentId == null && request.ConsultationId == null)
                throw new BusinessException("MedicalRecord.MustLinkToAppointmentOrConsultation");
            if (request.AppointmentId != null && request.ConsultationId != null)
                throw new BusinessException("MedicalRecord.CannotLinkToBoth");

            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var doctor = await db.Doctors
               .Include(u => u.user)
               .FirstOrDefaultAsync(d => d.userId == userId 
                                    && d.user.isActive);
            if (doctor == null)
                throw new BusinessException("Doctor.NotFound");

            var totalCost = 0.0;
            var billAmount = 0.0;
            DateTime Create;
            if (request.AppointmentId != null)
            {
                var doctorAppointment = await db.Appointments
                .FirstOrDefaultAsync(d => d.patientId == request.PatientId
                                     && d.doctorId == doctor.Id
                                     && d.Id == request.AppointmentId
                                     && d.appointmentStatus != AppointmentStatus.Cancelled);

                if (doctorAppointment == null)
                    throw new BusinessException("Appointment.NotFound");

                var record = new MedicalRecord
                {
                    Diagnosis = request.Diagnosis,
                    Notes = request.Notes,
                    Prescription = request.Prescription,
                    AppointmentId = request.AppointmentId,
                    CreatedAt = DateTime.UtcNow,
                    DoctorId = doctor.Id,
                    PatientId = request.PatientId,
                    UpdatedAt = DateTime.UtcNow,
                };
                
                await db.MedicalRecords.AddAsync(record);
                Create = record.CreatedAt;
                if (request.ConsultationCost > 0)
                    doctorAppointment.ConsultationCost = (decimal)request.ConsultationCost;

                if (request.Items != null)
                {
                    foreach (var item in request.Items)
                    {
                        var Item = new AppointmentItem
                        {
                            ServicePriceId = item.Id,
                            UnitPrice = item.UnitPrice,
                            Quantity = item.Quantity,
                            TotalPrice = item.UnitPrice * item.Quantity,
                            AppointmentId = doctorAppointment.Id,
                        };
                        await db.AppointmentItems.AddAsync(Item);
                        totalCost += (double)Item.TotalPrice;

                    }

                    billAmount = (double)((decimal)totalCost + doctorAppointment.ConsultationCost);

                }
                else
                {
                    var serviceCost = await db.ServicePrices
                        .FirstOrDefaultAsync(s => s.Type == ServiceType.Appointment
                                             && s.IsActive);

                    if (serviceCost == null)
                        throw new BusinessException("ServicePrice.NotFound");
                    billAmount = totalCost + (double)serviceCost.Price;
                    
                }
                var payment = new Payment
                {
                    Amount = (decimal)billAmount,
                    PatientId = request.PatientId,
                    Type = PaymentType.Appointment,
                    Status = PaymentStatus.Pending,
                    AppointmentId = doctorAppointment.Id
                };
                await db.Payments.AddAsync(payment);
                doctorAppointment.BillAmount = (decimal?)billAmount;
            }
            else
            {
                var doctorConsultation = await db.Consultations
                  .FirstOrDefaultAsync(c => c.DoctorId == doctor.Id
                          && c.PatientId == request.PatientId
                          && c.Id == request.ConsultationId
                          && c.Status != ConsultationStatus.Rejected);

                if (doctorConsultation == null)
                    throw new BusinessException("Consultation.NotFound");

                var record = new MedicalRecord
                {
                    Diagnosis = request.Diagnosis,
                    Notes = request.Notes,
                    Prescription = request.Prescription,
                    ConsultationId = request.ConsultationId,
                    CreatedAt = DateTime.UtcNow,
                    DoctorId = doctor.Id,
                    PatientId = request.PatientId,
                    UpdatedAt = DateTime.UtcNow,

                };
                await db.MedicalRecords.AddAsync(record);
                Create = record.CreatedAt;

                var serviceCost = await db.ServicePrices
                    .FirstOrDefaultAsync(s => s.Type == ServiceType.ConsultationCost
                                         && s.IsActive);

                if (serviceCost == null)
                    throw new BusinessException("ServicePrice.NotFound");

                billAmount = (double)serviceCost.Price; 
            }

            await db.SaveChangesAsync();

            return new MedicalRecordDetailResponseDto
            {

                PatientId = request.PatientId,
                DoctorId = userId,
                Diagnosis = request.Diagnosis,
                BillAmount = (decimal?)billAmount,
                DoctorName = $"{doctor.user.firstName} {doctor.user.lastName}",
                AppointmentId = request.AppointmentId,
                ConsultationCost = request.ConsultationCost,
                ConsultationId = request.ConsultationId,
                Prescription = request.Prescription,
                Notes = request.Notes,
                CreatedAt = Create,
                Items = request.Items?.Select(p => new MedicalRecordItemDto
                {
                    TotalPrice = (decimal)totalCost,
                    UnitPrice = p.UnitPrice,
                    Quantity = p.Quantity,
                    ServiceName = p.ServiceName,
                    Id = p.Id,
                }).ToList(),
                UpdateAt = DateTime.UtcNow,
            };
        }

        public async Task<MedicalRecordDetailResponseDto> EditMedicalRecordAsync(int centerId, int userId, UpdateMedicalRecordRequestDto request)
        {
            if (request.AppointmentId == null && request.ConsultationId == null)
                throw new BusinessException("MedicalRecord.MustLinkToAppointmentOrConsultation");
            if (request.AppointmentId != null && request.ConsultationId != null)
                throw new BusinessException("MedicalRecord.CannotLinkToBoth");

            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);


            var doctor = await db.Doctors
                .Include(u => u.user)
                .FirstOrDefaultAsync(d => d.userId == userId && d.user.isActive);
            if (doctor == null)
                throw new BusinessException("Doctor.NotFound");

            var record = await db.MedicalRecords
                .FirstOrDefaultAsync(m => m.Id == request.MedicalRecordId);

            if (record == null)
                throw new BusinessException("Medical.NotFound");

            var billAmount = 0.0;
            var totalCost = 0.0;
            DateTime Create;
            if (request.AppointmentId != null)
            {
                var appointment = await db.Appointments
                    .FirstOrDefaultAsync(a => a.Id == request.AppointmentId
                                         && a.patientId == request.PatientId
                                         && a.doctorId == doctor.Id
                                         && a.appointmentStatus != AppointmentStatus.Cancelled);

                if (appointment == null)
                    throw new BusinessException("Appointment.NotFound");

                billAmount = (double)appointment.BillAmount;

                if (request.Prescription != null)
                    record.Prescription = request.Prescription;

                if (request.Diagnosis != null)
                    record.Diagnosis = request.Diagnosis;

                if (request.RecordNotes != null)
                    record.Notes = request.RecordNotes;

                var appointmentItem = await db.AppointmentItems
                    .Where(a => a.AppointmentId == request.AppointmentId)
                    .ToListAsync();

                if (request.RemoveItemIds != null && appointmentItem != null)
                {
                    foreach (var removeItemId in request.RemoveItemIds)
                    {
                        var itemToRemove = appointmentItem.FirstOrDefault(ai => ai.Id == removeItemId);
                        if (itemToRemove != null)
                        {
                            db.AppointmentItems.Remove(itemToRemove);
                            billAmount -= (double)(itemToRemove.UnitPrice * itemToRemove.Quantity);
                        }
                    }
                }

                if (request.CustomConsultationPrice > 0)
                {
                    if (appointment.ConsultationCost != null)
                    {
                        billAmount -= (double)appointment.ConsultationCost;
                    }
                    appointment.ConsultationCost = (decimal)request.CustomConsultationPrice;
                    billAmount += (double)appointment.ConsultationCost;
                }

                if (request.Items != null)
                {
                    foreach (var item in request.Items)
                    {
                        var Item = new AppointmentItem
                        {
                            ServicePriceId = item.Id,
                            UnitPrice = item.UnitPrice,
                            Quantity = item.Quantity,
                            TotalPrice = item.UnitPrice * item.Quantity,
                            AppointmentId = appointment.Id,
                        };
                        await db.AppointmentItems.AddAsync(Item);
                        billAmount += (double)Item.TotalPrice;
                    }
                }
                var payment = await db.Payments
                    .FirstOrDefaultAsync(p => p.AppointmentId == request.AppointmentId);

                if (payment == null)
                    throw new BusinessException("Payment.NotFound");

                payment.Amount = (decimal)billAmount;
                if (request.PaymentNotes != null)
                    payment.Notes = request.PaymentNotes;

                appointment.BillAmount = (decimal?)billAmount;
                Create = record.CreatedAt;
            }

            else 
            {
                var consultation = await db.Consultations
                    .FirstOrDefaultAsync(c => c.Id == request.ConsultationId
                                         && c.DoctorId == doctor.Id
                                         && c.PatientId == request.PatientId
                                         && c.Status != ConsultationStatus.Rejected);

                if (consultation == null)
                    throw new BusinessException("Consultation.NotFound");

                if (request.Prescription != null)
                    record.Prescription = request.Prescription;

                if (request.Diagnosis != null)
                    record.Diagnosis = request.Diagnosis;

                if (request.RecordNotes != null)
                    record.Notes = request.RecordNotes;
                Create = record.CreatedAt;
            }
            await db.SaveChangesAsync();

            return new MedicalRecordDetailResponseDto
            {

                PatientId = request.PatientId,
                DoctorId = userId,
                Diagnosis = request.Diagnosis,
                BillAmount = (decimal?)billAmount,
                DoctorName = $"{doctor.user.firstName} {doctor.user.lastName}",
                AppointmentId = request.AppointmentId,
                ConsultationCost = request.CustomConsultationPrice,
                ConsultationId = request.ConsultationId,
                Prescription = record.Prescription,
                Notes = record.Notes,
                CreatedAt = Create,
                UpdateAt = DateTime.UtcNow,
                Items = request.Items?.Select(p => new MedicalRecordItemDto
                {
                    TotalPrice = (decimal)totalCost,
                    UnitPrice = p.UnitPrice,
                    Quantity = p.Quantity,
                    ServiceName = p.ServiceName,
                    Id = p.Id,
                }).ToList(),

            };

        }

        public async Task<Application.Common.PagedResult<MedicalRecordDetailResponseDto>> GetPatientMedicalHistoryAsync(
        int centerId, int userId, int patientId, PagedRequest request)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId && c.CenterStatus == CenterStatus.Active);
            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var doctor = await db.Doctors
                .Include(u => u.user)
                .FirstOrDefaultAsync(d => d.userId == userId && d.user.isActive);
            if (doctor == null)
                throw new BusinessException("Doctor.NotFound");

            var patientExists = await db.Patients
                .AnyAsync(p => p.patientId == patientId 
                          && p.user.isActive);
            if (!patientExists)
                throw new BusinessException("Patient.NotFound");

            var query = db.MedicalRecords
                .Where(r => r.PatientId == patientId)
                .OrderByDescending(r => r.CreatedAt)
                .Select(record => new MedicalRecordDetailResponseDto
                {
                    Id = record.Id,
                    PatientId = record.PatientId,
                    DoctorId = record.DoctorId,
                    DoctorName = record.Doctor.user.firstName + " " + record.Doctor.user.lastName,
                    AppointmentId = record.AppointmentId,
                    ConsultationId = record.ConsultationId,
                    Diagnosis = record.Diagnosis,
                    Prescription = record.Prescription,
                    Notes = record.Notes,
                    ConsultationCost = record.Appointment != null ? record.Appointment.ConsultationCost : null,
                    BillAmount = record.Appointment != null ? record.Appointment.BillAmount : null,
                    Items = record.Appointment != null && record.Appointment.Items != null
                        ? record.Appointment.Items.Select(i => new MedicalRecordItemDto
                        {
                            Id = i.Id,
                            ServiceName = i.ServicePrice.ServiceName,
                            Quantity = i.Quantity,
                            UnitPrice = i.UnitPrice,
                            TotalPrice = i.TotalPrice
                        }).ToList()
                        : null,
                    CreatedAt = record.CreatedAt,
                    UpdateAt = record.UpdatedAt,
                });

            return await query.ToPagedResultAsync(request.PageNumber, request.PageSize);
        }

        public async Task<MedicalRecordDetailResponseDto> GetMedicalRecordByIdAsync(int centerId, int userId, int medicalRecordId)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId && c.CenterStatus == CenterStatus.Active);
            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var doctor = await db.Doctors
                .Include(u => u.user)
                .FirstOrDefaultAsync(d => d.userId == userId 
                                     && d.user.isActive);
            if (doctor == null)
                throw new BusinessException("Doctor.NotFound");

            var record = await db.MedicalRecords
                .Include(r => r.Doctor)
                .ThenInclude(d => d.user)
                .Include(r => r.Appointment)
                .ThenInclude(a => a!.Items)
                .ThenInclude(i => i.ServicePrice)
                .FirstOrDefaultAsync(r => r.Id == medicalRecordId);

            if (record == null)
                throw new BusinessException("MedicalRecord.NotFound");

            return new MedicalRecordDetailResponseDto
            {
                Id = record.Id,
                PatientId = record.PatientId,
                DoctorId = record.DoctorId,
                DoctorName = $"{record.Doctor.user.firstName} {record.Doctor.user.lastName}",
                AppointmentId = record.AppointmentId,
                ConsultationId = record.ConsultationId,
                Diagnosis = record.Diagnosis,
                Prescription = record.Prescription,
                Notes = record.Notes,
                ConsultationCost = record.Appointment?.ConsultationCost,
                BillAmount = record.Appointment?.BillAmount,
                Items = record.Appointment?.Items?.Select(i => new MedicalRecordItemDto
                {
                    Id = i.Id,
                    ServiceName = i.ServicePrice.ServiceName,
                    Quantity = i.Quantity,
                    UnitPrice = i.UnitPrice,
                    TotalPrice = i.TotalPrice
                }).ToList(),
                CreatedAt = record.CreatedAt,
                UpdateAt = record.UpdatedAt,
            };
        }
    }

}
