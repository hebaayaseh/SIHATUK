
using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.ConfirmPaymentDto;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.Interfaces.IConfirmPayment;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.PaymentEnums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;

namespace Sehatak.Infrastructure.Services.ConfirmPaymentService
{
    public class ConfirmPaymentService : IConfirmPayment
    {
        private readonly SharedDbContext sharedDbContext;
        private readonly TenantDbContextFactory contextFactory;
        public ConfirmPaymentService(SharedDbContext sharedDbContext, TenantDbContextFactory contextFactory)
        {
            this.sharedDbContext = sharedDbContext;
            this.contextFactory = contextFactory;
        }

        public async Task<PaymentResponseDto> ReceptionistCollectAppointmentPaymentAsync(int centerId, int userId, int appointmentId, CollectPaymentRequestDto request)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var user = await db.Users
                .FirstOrDefaultAsync(u => u.Id == userId
                                     && u.isActive);

            var payment = await db.Payments
                .FirstOrDefaultAsync(p => p.AppointmentId == appointmentId
                             && p.Status == PaymentStatus.Pending);

            if (payment == null)
                throw new BusinessException("Payment.NotFound");

            payment.Status = PaymentStatus.Paid;
            payment.Method = request.Method;
            payment.RecordedByStaffId = userId;

            var appointment = await db.Appointments
                .FirstOrDefaultAsync(a => a.Id == appointmentId);

            if (appointment == null)
                throw new BusinessException("Appointment.NotFound");

            if (appointment.actualStartTime == null)
                throw new BusinessException("Appointment.NotCheckedInYet");

            if (appointment.CheckOutTime != null)
                throw new BusinessException("Appointment.AlreadyCheckedOut");


            appointment.CheckOutTime = DateTime.UtcNow;
            appointment.appointmentStatus = AppointmentStatus.Completed;

            await db.SaveChangesAsync();

            return new PaymentResponseDto
            {
                PaymentId = payment.Id,
                Amount = payment.Amount,
                Status = payment.Status.ToString(),
                Method = payment.Method.ToString(),
                PaidAt = payment.PaidAt
            };
        }

        public async Task<PaymentResponseDto> ReceptionistPayLabRequestAsync(int centerId, int userId, int labRequestId, CollectPaymentRequestDto request)
        {
            var center = await sharedDbContext.MedicalCenters
                .FirstOrDefaultAsync(c => c.Id == centerId
                                     && c.CenterStatus == CenterStatus.Active);

            if (center == null)
                throw new BusinessException("Center.NotFound");

            using var db = contextFactory.CreateForCenter(centerId);

            var user = await db.Users
                .FirstOrDefaultAsync(u => u.Id == userId
                                     && u.isActive);

            if (user == null)
                throw new BusinessException("User.NotFound");

            var labRequest = await db.LabRequests
                .Include(p => p.Payment)
                .Include(i => i.Items)
                .FirstOrDefaultAsync(l => l.Id == labRequestId);

            if (labRequest == null)
                throw new BusinessException("LabRequest.NotFound");


            if (labRequest.Payment != null)
                throw new BusinessException("LabRequest.AlreadyPaid");


            var amount = labRequest.Items.Sum(i => i.UnitPrice);

            var payment = new Payment
            {
                PatientId = labRequest.PatientId,
                LabRequestId = labRequest.Id,
                Amount = amount,
                Type = PaymentType.Lab,
                Method = request.Method,
                Status = PaymentStatus.Paid,
                RecordedByStaffId = userId,
                PaidAt = DateTime.UtcNow
            };

            await db.Payments.AddAsync(payment);
            await db.SaveChangesAsync();

            return new PaymentResponseDto
            {
                PaymentId = payment.Id,
                Amount = payment.Amount,
                Status = payment.Status.ToString(),
                Method = payment.Method.ToString(),
                PaidAt = payment.PaidAt
            };
        }
    }
}
