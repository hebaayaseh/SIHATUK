using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Sehatak.Domain.Entities.TenantEntities;
using Shouldly;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.PaymentEnums;
using Sehatak.IntegrationTests.Infrastructure;
using Sehatak.IntegrationTests.Payments;
using LabSvc = Sehatak.Infrastructure.Services.LabService.LabService;

namespace Sehatak.IntegrationTests.Labs;

/// <summary>
/// Center + doctor + patient + receptionist (from the payment base) plus a lab technician,
/// three lab tests in the price list (10 / 20 / 30) and one appointment for the doctor and patient.
/// </summary>
public abstract class LabTestBase(SqlServerFixture fx) : PaymentTestBase(fx)
{
    protected int Tech { get; private set; }
    protected int Svc1 { get; private set; }   // price 10
    protected int Svc2 { get; private set; }   // price 20
    protected int Svc3 { get; private set; }   // price 30
    protected int Appt { get; private set; }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        Tech = await C.AddLabTechnicianAsync();      // ? ??? ?????
        Svc1 = await AddServiceAsync("CBC", 10m);
        Svc2 = await AddServiceAsync("Glucose", 20m);
        Svc3 = await AddServiceAsync("Vitamin D", 30m);
        Appt = await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900);
    }

    protected LabSvc NewLabService() => new(Fx.Harness.NewSharedDb(), Fx.Harness.TenantFactory, C.Audit);

    /// <summary>Seeds a lab request directly in the database. Default items: Svc1 (10) and Svc2 (20), both available.</summary>
    protected async Task<(int Id, int[] ItemIds)> SeedLabAsync(
        LabRequestStatus status = LabRequestStatus.Pending,
        int? patientId = null, int? requestedBy = null, int? appointmentId = null,
        bool noItems = false,
        params (int ServicePriceId, decimal UnitPrice, bool Available)[] items)
    {
        if (items.Length == 0 && !noItems)
            items = [(Svc1, 10m, true), (Svc2, 20m, true)];

        await using var db = Db();
        var lab = new LabRequest
        {
            PatientId = patientId ?? Pat.PatientId, DoctorId = Doc.DoctorId,
            RequestedByUserId = requestedBy ?? Doc.UserId,
            AppointmentId = appointmentId ?? Appt,
            Status = status, UpdatedAt = DateTime.UtcNow.AddHours(-1),
            Items = items.Select(i => new LabRequestItem
            {
                ServicePriceId = i.ServicePriceId, UnitPrice = i.UnitPrice, IsAvailable = i.Available
            }).ToList()
        };
        db.LabRequests.Add(lab);
        await db.SaveChangesAsync();
        return (lab.Id, lab.Items.OrderBy(i => i.Id).Select(i => i.Id).ToArray());
    }

    /// <summary>A request the technician can upload results for: Processing + a Paid lab payment.</summary>
    protected async Task<(int Id, int[] ItemIds)> SeedProcessingLabAsync(int? patientId = null,
        params (int ServicePriceId, decimal UnitPrice, bool Available)[] items)
    {
        var lab = await SeedLabAsync(LabRequestStatus.Processing, patientId, items: items);
        await AddPaymentAsync(patientId ?? Pat.PatientId, 30m, PaymentType.Lab, PaymentStatus.Paid, labRequestId: lab.Id);
        return lab;
    }

    protected static IFormFile FakeFile(string fileName, long length = 1024)
    {
        var f = Substitute.For<IFormFile>();
        f.FileName.Returns(fileName);
        f.Length.Returns(length);
        f.CopyToAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
         .Returns(ci => ci.Arg<Stream>().WriteAsync(new byte[] { 1, 2, 3 }, 0, 3));
        return f;
    }

    protected async Task<LabRequest> LoadLabAsync(int id)
    {
        await using var db = Db();
        return await db.LabRequests.Include(l => l.Items).AsNoTracking().SingleAsync(l => l.Id == id);
    }

    protected async Task<int> CountAuditAsync(string action, int entityId)
    {
        await using var db = Db();
        return await db.AuditLogs.CountAsync(a => a.Action == action && a.EntityId == entityId);
    }

    protected static void ShouldBeNowUtc(DateTime value) =>
        value.ShouldBeInRange(DateTime.UtcNow.AddMinutes(-2), DateTime.UtcNow.AddMinutes(1));
}
