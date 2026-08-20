using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.DTOs.MedicalRecordDto;
using Sehatak.Application.Interfaces;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

        public async Task<MedicalRecordResponseDto> AddMedicalRecordAsync(int centerId,int userId, MedicalReqordRequestDto request)
        {
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

            if (request.AppointmentId == null && request.ConsultationId == null)
                throw new BusinessException("MedicalRecord.MustLinkToAppointmentOrConsultation");

            if (request.AppointmentId != null && request.ConsultationId != null)
                throw new BusinessException("MedicalRecord.CannotLinkToBoth");

            if(request.AppointmentId!=null)
            {
                var appointment = await db.Appointments
                    .FirstOrDefaultAsync(d => d.Id == request.AppointmentId
                                         && d.doctorId == doctor.Id
                                         && (d.appointmentStatus == AppointmentStatus.CheckedIn
                                         || d.appointmentStatus == AppointmentStatus.Confirmed));
                if (appointment == null)
                    throw new BusinessException("Appointment.NotFound");
                decimal consultationPrice;
                if (request.CustomConsultationPrice !=null)
                {
                    consultationPrice = (decimal)request.CustomConsultationPrice ; 
                }
                else
                {
                    var defaultAppointmentPrice = await db.ServicePrices
                        .FirstOrDefaultAsync(s => s.Type == ServiceType.Appointment && s.IsActive);
                    if (defaultAppointmentPrice == null)
                        throw new BusinessException("ServicePrice.NotFound");

                    consultationPrice = defaultAppointmentPrice.Price;
                }
                appointment.ConsultationCost = consultationPrice;
                decimal itemsTotal = 0;
                int itemsQuantityTotal = 0;

                if (request.Items != null && request.Items.Any())
                {
                    foreach (var item in request.Items)
                    {
                        var service = await db.ServicePrices
                            .FirstOrDefaultAsync(s => s.Id == item.ServicePriceId && s.IsActive);
                        if (service == null)
                            throw new BusinessException("ServicePrice.NotFound");

                        var quantity = item.Quantity > 0 ? item.Quantity : 1;
                        var lineTotal = service.Price * quantity;

                        await db.AppointmentItems.AddAsync(new AppointmentItem
                        {
                            AppointmentId = appointment.Id,
                            ServicePriceId = item.ServicePriceId,
                            Quantity = quantity,
                            UnitPrice = service.Price,
                            TotalPrice = lineTotal
                        });

                        itemsTotal += lineTotal;
                        itemsQuantityTotal += quantity;
                    }
                }

                appointment.BillAmount = consultationPrice + itemsTotal;
                appointment.ItemsTotal = itemsQuantityTotal;
                appointment.appointmentStatus = AppointmentStatus.InProgress;
            }
                
            else
            {
                var consultation = await db.Consultations
                   .FirstOrDefaultAsync(c => c.Id == request.ConsultationId
                                        && c.DoctorId == doctor.Id
                                        && c.Status == ConsultationStatus.Accepted);
                 if (consultation == null)
                    throw new BusinessException("Consultation.NotFound");
            }

            
            var record = new MedicalRecord
            {
                PatientId = request.PatientId,
                DoctorId = doctor.Id,
                AppointmentId = request.AppointmentId,
                ConsultationId = request.ConsultationId,
                Prescription = request.Prescription,
                Notes = request.Notes,
                Diagnosis = request.Diagnosis,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await db.MedicalRecords.AddAsync(record);
            await db.SaveChangesAsync();

            return new MedicalRecordResponseDto 
            { 
                Diagnosis = record.Diagnosis,
                Notes = record.Notes,
                Prescription = record.Prescription,
            };

        }

        public async Task<string> EditMedicalRecordAsync(int centerId, int userId, UpdateMedicalRecordRequestDto request)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            if (request.AppointmentId == null && request.ConsultationId == null)
                throw new BusinessException("MedicalRecord.MustLinkToAppointmentOrConsultation");

            if (request.AppointmentId != null && request.ConsultationId != null)
                throw new BusinessException("MedicalRecord.CannotLinkToBoth");


            var doctor = await db.Doctors
                .Include(u => u.user)
                .FirstOrDefaultAsync(d => d.userId == userId
                                     && d.user.isActive);

            if (doctor == null)
                throw new BusinessException("Doctor.NotFound");

            var record = await db.MedicalRecords
                    .FirstOrDefaultAsync(a => a.Id == request.MedicalRecordId
                                         && a.DoctorId == doctor.Id
                                         && a.PatientId == request.PatientId
                                         && (a.AppointmentId == request.AppointmentId
                                         || a.ConsultationId == request.ConsultationId));

            if (record == null)
                throw new BusinessException("MedicalRecord.NotFound");

            if (request.AppointmentId != null)
            {
                var appointment = await db.Appointments
                    .FirstOrDefaultAsync(d => d.Id == request.AppointmentId
                                         && d.doctorId == doctor.Id
                                         && (d.appointmentStatus == AppointmentStatus.InProgress
                                         || d.appointmentStatus == AppointmentStatus.CheckedIn
                                         || d.appointmentStatus == AppointmentStatus.Confirmed));

                if (appointment == null)
                    throw new BusinessException("Appointment.NotFound");

                if (request.RemoveItemIds != null && request.RemoveItemIds.Any())
                {
                    var itemsToRemove = await db.AppointmentItems
                        .Where(i => request.RemoveItemIds.Contains(i.Id)
                               && i.AppointmentId == appointment.Id)
                        .ToListAsync();

                    db.AppointmentItems.RemoveRange(itemsToRemove);
                }

                if (request.CustomConsultationPrice != null)
                {
                    appointment.ConsultationCost = request.CustomConsultationPrice.Value;
                }

                if (request.Items != null && request.Items.Any())
                {
                    foreach (var item in request.Items)
                    {
                        var service = await db.ServicePrices
                            .FirstOrDefaultAsync(s => s.Id == item.ServicePriceId 
                                                 && s.IsActive);

                        if (service == null)
                            throw new BusinessException("ServicePrice.NotFound");

                        var quantity = item.Quantity > 0 ? item.Quantity : 1;

                        await db.AppointmentItems.AddAsync(new AppointmentItem
                        {
                            AppointmentId = appointment.Id,
                            ServicePriceId = item.ServicePriceId,
                            Quantity = quantity,
                            UnitPrice = service.Price,
                            TotalPrice = service.Price * quantity
                        });
                    }
                }
                await db.SaveChangesAsync();

                var remainingItems = await db.AppointmentItems
                     .Where(i => i.AppointmentId == appointment.Id)
                     .ToListAsync();

                var itemsTotal = remainingItems.Sum(i => i.TotalPrice);
                var itemsQuantityTotal = remainingItems.Sum(i => i.Quantity);

                appointment.BillAmount = appointment.ConsultationCost + itemsTotal;
                appointment.ItemsTotal = itemsQuantityTotal;
                appointment.appointmentStatus = AppointmentStatus.InProgress;
            }
            if (request.Prescription != null)
                record.Prescription = request.Prescription;

            if (request.Notes != null)
                record.Notes = request.Notes;

            if (request.Diagnosis != null)
                record.Diagnosis = record.Diagnosis;

            record.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync();

            return "تم التعديل بنجاح";

        }
    }
}
