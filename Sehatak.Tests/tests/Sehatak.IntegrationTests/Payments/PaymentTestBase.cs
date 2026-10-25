using Microsoft.EntityFrameworkCore;
using Sehatak.Domain.Entities;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.PaymentEnums;
using Sehatak.IntegrationTests.Appointments;
using Sehatak.IntegrationTests.Infrastructure;
using BillSvc = Sehatak.Infrastructure.Services.BillPaymentService.BillPaymentService;
using ConfirmSvc = Sehatak.Infrastructure.Services.ConfirmPaymentService.ConfirmPaymentService;

namespace Sehatak.IntegrationTests.Payments;

/// <summary>
/// Same center/doctor/patient scenario as the appointment tests, plus a receptionist and
/// seeding helpers for payments, services, lab requests and appointment items.
/// </summary>
public abstract class PaymentTestBase(SqlServerFixture fx) : AppointmentTestBase(fx)
{
    protected int Rec { get; private set; }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        Rec = await C.AddReceptionistAsync();
    }

    protected ConfirmSvc NewConfirmService() => new(Fx.Harness.NewSharedDb(), Fx.Harness.TenantFactory, C.Audit);
    protected BillSvc NewBillService() => new(Fx.Harness.NewSharedDb(), Fx.Harness.TenantFactory);

    protected async Task<int> AddServiceAsync(string name, decimal price, ServiceType type = ServiceType.LabTest)
    {
        await using var db = Db();
        var s = new ServicePrice { ServiceName = name, Price = price, Type = type };
        db.ServicePrices.Add(s);
        await db.SaveChangesAsync();
        return s.Id;
    }

    protected async Task<int> AddPaymentAsync(int patientId, decimal amount, PaymentType type,
        PaymentStatus status = PaymentStatus.Pending, int? appointmentId = null, int? labRequestId = null,
        PaymentMethod method = PaymentMethod.cash, DateTime? paidAtUtc = null)
    {
        await using var db = Db();
        var p = new Payment
        {
            PatientId = patientId, Amount = amount, Type = type, Status = status,
            AppointmentId = appointmentId, LabRequestId = labRequestId, Method = method,
            PaidAt = paidAtUtc ?? DateTime.UtcNow
        };
        db.Payments.Add(p);
        await db.SaveChangesAsync();
        return p.Id;
    }

    protected async Task<int> AddLabRequestAsync(int patientId, LabRequestStatus status,
        params (int ServicePriceId, decimal UnitPrice, bool Available)[] items)
    {
        await using var db = Db();
        var l = new LabRequest
        {
            PatientId = patientId, DoctorId = Doc.DoctorId, RequestedByUserId = Doc.UserId,
            Status = status, UpdatedAt = DateTime.UtcNow,
            Items = items.Select(i => new LabRequestItem
            {
                ServicePriceId = i.ServicePriceId, UnitPrice = i.UnitPrice, IsAvailable = i.Available
            }).ToList()
        };
        db.LabRequests.Add(l);
        await db.SaveChangesAsync();
        return l.Id;
    }

    protected async Task AddAppointmentItemAsync(int appointmentId, int servicePriceId, int quantity, decimal unitPrice)
    {
        await using var db = Db();
        db.Set<AppointmentItem>().Add(new AppointmentItem
        {
            AppointmentId = appointmentId, ServicePriceId = servicePriceId,
            Quantity = quantity, UnitPrice = unitPrice, TotalPrice = quantity * unitPrice
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Marks the appointment as started (patient is with the doctor).</summary>
    protected async Task CheckInAsync(int appointmentId, bool alsoCheckedOut = false)
    {
        await using var db = Db();
        var a = await db.Appointments.SingleAsync(x => x.Id == appointmentId);
        a.actualStartTime = DateTime.UtcNow.AddMinutes(-20);
        if (alsoCheckedOut) a.CheckOutTime = DateTime.UtcNow.AddMinutes(-5);
        await db.SaveChangesAsync();
    }
}
