using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.MedicalRecordDto;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.IntegrationTests.Infrastructure;
using Sehatak.IntegrationTests.Payments;
using Shouldly;
using MedicalSvc = Sehatak.Infrastructure.Services.MedicalRecordService.MedicalRecordService;

namespace Sehatak.IntegrationTests.MedicalRecords;

/// <summary>
/// Center + doctor + patient + one Confirmed appointment (Appt) and a price list:
/// Appointment 50, FollowUp 30, ConsultationCost 60, and two extras (type Other): Bandage 5, Injection 12.
/// </summary>
public abstract class MedicalRecordTestBase(SqlServerFixture fx) : PaymentTestBase(fx)
{
    protected int Appt { get; private set; }
    protected int Bandage { get; private set; }     // 5
    protected int Injection { get; private set; }   // 12

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await AddServiceAsync("Visit", 50m, ServiceType.Appointment);
        await AddServiceAsync("Follow-up visit", 30m, ServiceType.FollowUp);
        await AddServiceAsync("Online consultation", 60m, ServiceType.ConsultationCost);
        Bandage = await AddServiceAsync("Bandage", 5m, ServiceType.Other);
        Injection = await AddServiceAsync("Injection", 12m, ServiceType.Other);
        Appt = await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900);
    }

    protected MedicalSvc NewRecordService() => new(Fx.Harness.NewSharedDb(), Fx.Harness.TenantFactory, C.Audit);

    protected static MedicalRecordItemDto It(int serviceId, int qty) => new() { Id = serviceId, Quantity = qty };

    protected MedicalRecordDetailRequestDto AppointmentReq(int? appointmentId = null, int? patientId = null, decimal? cost = null,
        params MedicalRecordItemDto[] items) => new()
    {
        PatientId = patientId ?? Pat.PatientId, AppointmentId = appointmentId ?? Appt,
        Diagnosis = "flu", Prescription = "rest", Notes = "n0",
        ConsultationCost = cost, Items = items.Length == 0 ? null : items.ToList()
    };

    protected MedicalRecordDetailRequestDto ConsultationReq(int consultationId, int? patientId = null) => new()
    {
        PatientId = patientId ?? Pat.PatientId, ConsultationId = consultationId,
        Diagnosis = "cough", Prescription = "syrup", Notes = "c0"
    };

    protected Task<MedicalRecordDetailResponseDto> Add(MedicalRecordDetailRequestDto req, int? doctorUserId = null) =>
        NewRecordService().AddMedicalRecordAsync(C.Id, doctorUserId ?? Doc.UserId, req);

    protected Task<MedicalRecordDetailResponseDto> Edit(UpdateMedicalRecordRequestDto req, int? doctorUserId = null) =>
        NewRecordService().EditMedicalRecordAsync(C.Id, doctorUserId ?? Doc.UserId, req);

    protected async Task<int> AddConsultationAsync(ConsultationStatus status = ConsultationStatus.Accepted,
        int? patientId = null, int? doctorId = null)
    {
        await using var db = Db();
        var c = new Consultation { PatientId = patientId ?? Pat.PatientId, DoctorId = doctorId ?? Doc.DoctorId, Status = status };
        db.Consultations.Add(c);
        await db.SaveChangesAsync();
        return c.Id;
    }

    /// <summary>A medical record written straight to the database (no payment, no bill).</summary>
    protected async Task<int> AddRecordDirectAsync(int? patientId = null, int? doctorId = null, int? appointmentId = null,
        int? consultationId = null, DateTime? createdUtc = null, string diagnosis = "d", string prescription = "p", string notes = "n")
    {
        await using var db = Db();
        var r = new MedicalRecord
        {
            PatientId = patientId ?? Pat.PatientId, DoctorId = doctorId ?? Doc.DoctorId,
            AppointmentId = appointmentId, ConsultationId = consultationId,
            Diagnosis = diagnosis, Prescription = prescription, Notes = notes,
            CreatedAt = createdUtc ?? DateTime.UtcNow, UpdatedAt = createdUtc ?? DateTime.UtcNow
        };
        db.MedicalRecords.Add(r);
        await db.SaveChangesAsync();
        return r.Id;
    }

    protected async Task<int> RecordIdForAppointmentAsync(int appointmentId)
    {
        await using var db = Db();
        return (await db.MedicalRecords.SingleAsync(r => r.AppointmentId == appointmentId)).Id;
    }

    protected async Task<int> RecordIdForConsultationAsync(int consultationId)
    {
        await using var db = Db();
        return (await db.MedicalRecords.SingleAsync(r => r.ConsultationId == consultationId)).Id;
    }

    protected async Task<int[]> AppointmentItemIdsAsync(int appointmentId)
    {
        await using var db = Db();
        return await db.AppointmentItems.Where(i => i.AppointmentId == appointmentId).OrderBy(i => i.Id).Select(i => i.Id).ToArrayAsync();
    }

    protected async Task<Payment> PaymentOfAsync(int appointmentId)
    {
        await using var db = Db();
        return await db.Payments.AsNoTracking().SingleAsync(p => p.AppointmentId == appointmentId);
    }

    protected async Task<Appointment> LoadAppointmentAsync(int appointmentId)
    {
        await using var db = Db();
        return await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == appointmentId);
    }

    protected async Task<MedicalRecord> LoadRecordAsync(int id)
    {
        await using var db = Db();
        return await db.MedicalRecords.AsNoTracking().SingleAsync(r => r.Id == id);
    }

    protected async Task<int> CountAuditAsync(string action) 
    {
        await using var db = Db();
        return await db.AuditLogs.CountAsync(a => a.Action == action);
    }

    protected static void ShouldBeNowUtc(DateTime value) =>
        value.ShouldBeInRange(DateTime.UtcNow.AddMinutes(-2), DateTime.UtcNow.AddMinutes(1));
}
