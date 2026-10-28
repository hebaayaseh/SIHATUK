using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Sehatak.Domain.Entities;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.PaymentEnums;
using Sehatak.IntegrationTests.Appointments;
using Sehatak.IntegrationTests.Infrastructure;
using ConsultSvc = Sehatak.Infrastructure.Services.Consultationservice.ConsultationService;

namespace Sehatak.IntegrationTests.Consultations;

/// <summary>
/// Same center/doctor/patient scenario, with the doctor enabled for online consultations
/// and one active "ConsultationCost" price of 60.
/// </summary>
public abstract class ConsultationTestBase(SqlServerFixture fx) : AppointmentTestBase(fx)
{
    // Audit action names exactly as the service writes them today (typos included).
    // If you fix the spelling in the service, change them here once.
    protected const string AuditRequest = "ConsultaionRequest";
    protected const string AuditCancel = "CancelConsultation";
    protected const string AuditRecordPayment = "ConsultaionRecorPayment";
    protected const string AuditSchedule = "DoctorScheduleConsultation";
    protected const string AuditRejectRequest = "RejectConsultationRequest";
    protected const string AuditConfirmPayment = "ConfirmPaymentConsultation";
    protected const string AuditRejectPayment = "RejectConsultationPayment";
    protected const string AuditComplete = "CompleteConsultation";

    protected static DateTime InTwoDays => DateTime.UtcNow.AddDays(2);

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await SetOnlineAsync(Doc.DoctorId, true);
        await AddConsultationPriceAsync(60m);
    }

    protected ConsultSvc NewConsultService() => new(Fx.Harness.NewSharedDb(), Fx.Harness.TenantFactory, C.Audit);

    protected async Task SetOnlineAsync(int doctorId, bool online)
    {
        await using var db = Db();
        (await db.Doctors.SingleAsync(d => d.Id == doctorId)).OnlineEnabled = online;
        await db.SaveChangesAsync();
    }

    protected async Task<int> AddConsultationPriceAsync(decimal price, bool active = true)
    {
        await using var db = Db();
        var s = new ServicePrice { ServiceName = "Online consultation", Price = price, Type = ServiceType.ConsultationCost, IsActive = active };
        db.ServicePrices.Add(s);
        await db.SaveChangesAsync();
        return s.Id;
    }

    protected async Task<int> AddPaymentAsync(int patientId, decimal amount, PaymentType type)
    {
        await using var db = Db();
        var p = new Payment { PatientId = patientId, Amount = amount, Type = type, Status = PaymentStatus.Pending, Method = PaymentMethod.cash, PaidAt = DateTime.UtcNow };
        db.Payments.Add(p);
        await db.SaveChangesAsync();
        return p.Id;
    }

    protected async Task DeactivateConsultationPricesAsync()
    {
        await using var db = Db();
        foreach (var p in await db.ServicePrices.Where(s => s.Type == ServiceType.ConsultationCost).ToListAsync())
            p.IsActive = false;
        await db.SaveChangesAsync();
    }

    protected async Task<int> AddConsultationAsync(int patientId, ConsultationStatus status = ConsultationStatus.Pending,
        DateTime? scheduledAt = null, int? doctorId = null, string? videoLink = null)
    {
        await using var db = Db();
        var c = new Consultation
        {
            PatientId = patientId, DoctorId = doctorId ?? Doc.DoctorId,
            Status = status, ScheduledAt = scheduledAt, VideoLink = videoLink
        };
        db.Consultations.Add(c);
        await db.SaveChangesAsync();
        return c.Id;
    }

    protected async Task<int> AddConsultationPaymentAsync(int consultationId, int patientId,
        PaymentStatus status = PaymentStatus.Pending, int? recordedByStaffId = null,
        string? reference = "REF-1", DateTime? paidAtUtc = null, string? receiptUrl = null)
    {
        await using var db = Db();
        var p = new Payment
        {
            PatientId = patientId, ConsultationId = consultationId, Amount = 60m, Type = PaymentType.Consultation,
            Method = PaymentMethod.online, Status = status, RecordedByStaffId = recordedByStaffId,
            ReferenceNumber = reference, ReceiptImageUrl = receiptUrl, PaidAt = paidAtUtc ?? DateTime.UtcNow
        };
        db.Payments.Add(p);
        await db.SaveChangesAsync();
        return p.Id;
    }

    /// <summary>A fake uploaded file: the service only reads FileName, Length and CopyToAsync.</summary>
    protected static IFormFile Upload(string fileName, long length = 1024)
    {
        var content = new byte[] { 1, 2, 3, 4 };
        var f = Substitute.For<IFormFile>();
        f.FileName.Returns(fileName);
        f.Length.Returns(length);
        f.CopyToAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
         .Returns(ci => ci.Arg<Stream>().WriteAsync(content, 0, content.Length));
        return f;
    }
}
