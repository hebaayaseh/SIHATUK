using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.BillPaymentDto;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.DTOs.LabDto;
using Sehatak.Application.DTOs.MedicalRecordDto;
using Sehatak.Application.Interfaces.IBillPayment;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums.PaymentEnums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;

namespace Sehatak.Infrastructure.Services.BillPaymentService
{
    public class BillPaymentService : IBillPayment
    {
        private readonly SharedDbContext sharedDbContext;
        private readonly TenantDbContextFactory contextFactory;
        public BillPaymentService(SharedDbContext sharedDbContext, TenantDbContextFactory contextFactory)
        {
            this.sharedDbContext = sharedDbContext;
            this.contextFactory = contextFactory;
        }
        public async Task<PagedResult<AppointmentBillPaymentResponseDto>> AppointmentsBillPayment(int centerId, int userId,PagedRequest request,int? subPatientId)
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
                    .FirstOrDefaultAsync(s => s.ParentPatientId == actingPatient.patientId
                                        && s.patientId == subPatientId);

                if (subPatient == null)
                    throw new BusinessException("SubPatient.NotFound");

                actingPatient = subPatient;
            }

            var query = db.Payments
                .Where(p => p.PatientId == actingPatient.patientId
                       && p.Type == PaymentType.Appointment)
                .Include(a=>a.Appointment)
                .OrderByDescending(p => p.PaidAt)
                .Select(a => new AppointmentBillPaymentResponseDto
                {
                    PaymentId = a.Id,
                    PatientId = actingPatient.patientId,
                    PatientName = actingPatient.userId!=null 
                    ? $"{actingPatient.user.firstName} {actingPatient.user.lastName}"
                    : $"{actingPatient.FirstName} {actingPatient.LastName}",
                    AppointmentId = a.AppointmentId,
                    BillPaymentStatus = a.Status.ToString(),
                    BillPaymentType = a.Type.ToString(),
                    Method = a.Method.ToString(),
                    PaidAt = a.PaidAt,
                    Items = a.Appointment.Items.Select(n=>new MedicalRecordItemResponseDto
                    {
                       Id = n.Id,
                       ServiceName = n.ServicePrice.ServiceName,
                       ServicePriceId = n.ServicePriceId,
                       Quantity = n.Quantity,
                       UnitPrice = n.UnitPrice,
                       TotalPrice = n.TotalPrice
                       
                    }).ToList(),
                    Amount = a.Amount
                });

            return await query.ToPagedResultAsync(request.PageNumber, request.PageSize);
        }

        public async Task<PagedResult<ConsultationBillPaymentResponseDto>> ConsultationSBillPayment(int centerId, int userId, PagedRequest request, int? subPatientId)
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
                    .FirstOrDefaultAsync(s => s.ParentPatientId == actingPatient.patientId
                                        && s.patientId == subPatientId);

                if (subPatient == null)
                    throw new BusinessException("SubPatient.NotFound");

                actingPatient = subPatient;
            }

            var query = db.Payments
                .Where(p => p.PatientId == actingPatient.patientId
                       && p.Type == PaymentType.Consultation)
                .Include(a => a.Consultation)
                .OrderByDescending(p => p.PaidAt)
                .Select(a => new ConsultationBillPaymentResponseDto
                {
                    PaymentId = a.Id,
                    PatientId = actingPatient.patientId,
                    PatientName = actingPatient.userId != null
                    ? $"{actingPatient.user.firstName} {actingPatient.user.lastName}"
                    : $"{actingPatient.FirstName} {actingPatient.LastName}",
                    ConsultaionId = a.ConsultationId,
                    BillPaymentStatus = a.Status.ToString(),
                    BillPaymentType = a.Type.ToString(),
                    Method = a.Method.ToString(),
                    PaidAt = a.PaidAt,
                    Amount = a.Amount
                });

            return await query.ToPagedResultAsync(request.PageNumber, request.PageSize);
        }

        public async Task<PagedResult<LabBillPaymentResponseDto>> LabsBillPayment(int centerId, int userId, PagedRequest request, int? subPatientId)
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
                    .FirstOrDefaultAsync(s => s.ParentPatientId == actingPatient.patientId
                                        && s.patientId == subPatientId);

                if (subPatient == null)
                    throw new BusinessException("SubPatient.NotFound");

                actingPatient = subPatient;
            }

            var query = db.Payments
                .Where(p => p.PatientId == actingPatient.patientId
                       && p.Type == PaymentType.Lab)
                .Include(a => a.LabRequest)
                .OrderByDescending(p => p.PaidAt)
                .Select(a => new LabBillPaymentResponseDto
                {
                    PaymentId = a.Id,
                    PatientId = actingPatient.patientId,
                    PatientName = actingPatient.userId != null
                    ? $"{actingPatient.user.firstName} {actingPatient.user.lastName}"
                    : $"{actingPatient.FirstName} {actingPatient.LastName}",
                    LabId = a.LabRequestId,
                    BillPaymentStatus = a.Status.ToString(),
                    BillPaymentType = a.Type.ToString(),
                    Method = a.Method.ToString(),
                    PaidAt = a.PaidAt,
                    Items = a.LabRequest.Items.Select(n => new LabItemResponseDto
                    {
                        ItemId = n.Id,
                        ServiceName = n.ServicePrice.ServiceName,
                        UnitPrice = n.UnitPrice,
                        ServicePriceId = n.ServicePriceId
                    }).ToList(),
                    Amount = a.Amount
                });

            return await query.ToPagedResultAsync(request.PageNumber, request.PageSize);
        }
    }
}
