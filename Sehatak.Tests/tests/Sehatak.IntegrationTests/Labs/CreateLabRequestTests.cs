using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.LabDto;
using Sehatak.Domain.Enums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Labs;

/// <summary>Creating lab requests: by the doctor (tied to an appointment) and by the receptionist (walk-in).</summary>
[Collection(SqlServerCollection.Name)]
public class CreateLabRequestTests(SqlServerFixture fx) : LabTestBase(fx)
{
    private static List<LabRequestItemsSummaryDto> Items(params int[] ids) => ids.Select(i => new LabRequestItemsSummaryDto { Id = i }).ToList();

    private Task<LabRequestResponseDto> DoctorCreate(CreateLabRequestDto dto, int? userId = null) =>
        NewLabService().CreateLabRequestAsync(C.Id, userId ?? Doc.UserId, dto);

    private CreateLabRequestDto Dto(params int[] svc) => new() { PatientId = Pat.PatientId, AppointmentId = Appt, Note = "fasting", LabItems = Items(svc) };

    // ------------------------------------------------------------------ Doctor: CreateLabRequestAsync

    [Fact]
    public async Task Doctor_creates_a_Pending_request_with_priced_items()
    {
        var r = await DoctorCreate(Dto(Svc1, Svc3));

        r.LabStatus.ShouldBe("Pending");
        r.TotalPrice.ShouldBe(40m);
        r.LabItems!.Select(i => i.ServicePriceId).ShouldBe(new[] { Svc1, Svc3 }, ignoreOrder: true);
        r.LabItems!.ShouldAllBe(i => i.IsAvailable);
        r.PatientName.ShouldNotBeNullOrWhiteSpace();
        r.AppointmentId.ShouldBe(Appt);
        r.Note.ShouldBe("fasting");

        var lab = await LoadLabAsync(r.LabRequestId);
        lab.Status.ShouldBe(LabRequestStatus.Pending);
        lab.DoctorId.ShouldBe(Doc.DoctorId);
        lab.RequestedByUserId.ShouldBe(Doc.UserId);
        lab.PatientId.ShouldBe(Pat.PatientId);
        lab.AppointmentId.ShouldBe(Appt);
        lab.Items.Count.ShouldBe(2);
        lab.Items.Sum(i => i.UnitPrice).ShouldBe(40m);
        ShouldBeNowUtc(lab.RequstedAt);
        ShouldBeNowUtc(lab.UpdatedAt);
        (await CountAuditAsync("CreateLabRequest", lab.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task Item_price_is_a_snapshot_and_does_not_follow_later_price_changes()
    {
        var r = await DoctorCreate(Dto(Svc1));

        await using (var db = Db())
        {
            (await db.ServicePrices.SingleAsync(s => s.Id == Svc1)).Price = 99m;
            await db.SaveChangesAsync();
        }

        (await LoadLabAsync(r.LabRequestId)).Items.Single().UnitPrice.ShouldBe(10m);
    }

    /// <summary>The response is built before the items get their database ids, so every ItemId comes back as 0.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Doctor_response_contains_the_real_item_ids()
    {
        var r = await DoctorCreate(Dto(Svc1, Svc2));
        var saved = (await LoadLabAsync(r.LabRequestId)).Items.Select(i => i.Id).ToArray();

        r.LabItems!.Select(i => i.ItemId).ShouldBe(saved, ignoreOrder: true);
    }

    [Fact]
    public async Task Doctor_can_create_for_a_child_patient()
    {
        var sub = await C.AddSubPatientAsync(Pat);
        var appt = await C.AddAppointmentAsync(Doc.DoctorId, sub.PatientId, Date, S0930);

        var r = await DoctorCreate(new CreateLabRequestDto { PatientId = sub.PatientId, AppointmentId = appt, LabItems = Items(Svc1) });

        r.PatientId.ShouldBe(sub.PatientId);
        r.PatientName.ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// A request with no tests can be created, but it can never be collected (LabRequest.NoAvailableItems), so it sits in
    /// the lab queue forever. Suggested rule: reject it at creation. The error code is my suggestion - rename as you like.
    /// </summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_request_without_any_test_is_refused()
    {
        await ShouldFailWith("LabRequest.NoItems", () => DoctorCreate(new CreateLabRequestDto { PatientId = Pat.PatientId, AppointmentId = Appt, LabItems = null }));
        await ShouldFailWith("LabRequest.NoItems", () => DoctorCreate(new CreateLabRequestDto { PatientId = Pat.PatientId, AppointmentId = Appt, LabItems = [] }));
    }

    [Fact]
    public async Task Duplicate_item_is_refused_and_nothing_is_saved()
    {
        await ShouldFailWith("LabRequest.DuplicateItem", () => DoctorCreate(Dto(Svc1, Svc2, Svc1)));

        await using var db = Db();
        (await db.LabRequests.CountAsync()).ShouldBe(0);
        (await db.LabRequestItems.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task ServicePrice_NotFound_for_an_unknown_service_and_nothing_is_saved()
    {
        await ShouldFailWith("ServicePrice.NotFound", () => DoctorCreate(Dto(Svc1, 999_999)));
        await using var db = Db();
        (await db.LabRequests.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task ServicePrice_NotFound_for_an_inactive_service()
    {
        await using (var db = Db())
        {
            (await db.ServicePrices.SingleAsync(s => s.Id == Svc1)).IsActive = false;
            await db.SaveChangesAsync();
        }
        await ShouldFailWith("ServicePrice.NotFound", () => DoctorCreate(Dto(Svc1)));
    }

    [Fact]
    public async Task ServicePrice_NotFound_when_the_service_is_not_a_lab_test()
    {
        var visit = await AddServiceAsync("Visit", 50m, ServiceType.Other);
        await ShouldFailWith("ServicePrice.NotFound", () => DoctorCreate(Dto(visit)));
    }

    [Fact]
    public async Task Appointment_NotFound_for_a_cancelled_appointment()
    {
        var cancelled = await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S1000, AppointmentStatus.Cancelled);
        await ShouldFailWith("Appointment.NotFound", () => DoctorCreate(new CreateLabRequestDto { PatientId = Pat.PatientId, AppointmentId = cancelled, LabItems = Items(Svc1) }));
    }

    [Fact]
    public async Task Appointment_NotFound_for_another_doctors_appointment()
    {
        var doc2 = await C.AddDoctorAsync();
        await ShouldFailWith("Appointment.NotFound", () => DoctorCreate(Dto(Svc1), doc2.UserId));
    }

    [Fact]
    public async Task Appointment_NotFound_when_the_appointment_belongs_to_another_patient()
    {
        var other = await C.AddPatientAsync();
        await ShouldFailWith("Appointment.NotFound", () => DoctorCreate(new CreateLabRequestDto { PatientId = other.PatientId, AppointmentId = Appt, LabItems = Items(Svc1) }));
    }

    [Fact]
    public Task Appointment_NotFound_for_an_unknown_appointment() =>
        ShouldFailWith("Appointment.NotFound", () => DoctorCreate(new CreateLabRequestDto { PatientId = Pat.PatientId, AppointmentId = 999_999, LabItems = Items(Svc1) }));

    [Fact]
    public Task Patient_NotFound_for_an_unknown_patient() =>
        ShouldFailWith("Patient.NotFound", () => DoctorCreate(new CreateLabRequestDto { PatientId = 999_999, AppointmentId = Appt, LabItems = Items(Svc1) }));

    [Fact]
    public async Task Patient_NotFound_for_an_inactive_patient()
    {
        var gone = await C.AddPatientAsync(active: false);
        await ShouldFailWith("Patient.NotFound", () => DoctorCreate(new CreateLabRequestDto { PatientId = gone.PatientId, AppointmentId = Appt, LabItems = Items(Svc1) }));
    }

    [Fact]
    public Task Doctor_NotFound_when_the_caller_is_not_a_doctor() =>
        ShouldFailWith("Doctor.NotFound", () => DoctorCreate(Dto(Svc1), Rec));

    [Fact]
    public async Task Doctor_NotFound_for_an_inactive_doctor()
    {
        var inactive = await C.AddDoctorAsync(active: false);
        await ShouldFailWith("Doctor.NotFound", () => DoctorCreate(Dto(Svc1), inactive.UserId));
    }

    [Fact]
    public Task Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => NewLabService().CreateLabRequestAsync(999_999, Doc.UserId, Dto(Svc1)));

    // ------------------------------------------------------------------ Receptionist: ReceptionistCreateLabRequestAsync

    private Task<ReceptionistLabRequestReponseDto> RecCreate(ReceptionistCreateLabRequestDto dto, int? userId = null) =>
        NewLabService().ReceptionistCreateLabRequestAsync(C.Id, userId ?? Rec, dto);

    private ReceptionistCreateLabRequestDto RecDto(params int[] svc) => new() { PatientId = Pat.PatientId, Note = "walk-in", LabItems = Items(svc) };

    [Fact]
    public async Task Receptionist_creates_a_walk_in_request_without_doctor_or_appointment()
    {
        var r = await RecCreate(RecDto(Svc2, Svc3));

        r.LabStatus.ShouldBe("Pending");
        r.TotalPrice.ShouldBe(50m);
        r.PatientName.ShouldNotBeNullOrWhiteSpace();

        var lab = await LoadLabAsync(r.LabRequestId);
        lab.DoctorId.ShouldBeNull();
        lab.AppointmentId.ShouldBeNull();
        lab.RequestedByUserId.ShouldBe(Rec);
        lab.Items.Count.ShouldBe(2);
        ShouldBeNowUtc(lab.RequstedAt);
        (await CountAuditAsync("ReceptionistCreateLabRequest", lab.Id)).ShouldBe(1);
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Receptionist_response_contains_the_real_item_ids()
    {
        var r = await RecCreate(RecDto(Svc1, Svc2));
        var saved = (await LoadLabAsync(r.LabRequestId)).Items.Select(i => i.Id).ToArray();
        r.LabItems!.Select(i => i.ItemId).ShouldBe(saved, ignoreOrder: true);
    }

    [Fact]
    public async Task Receptionist_can_create_for_a_child()
    {
        var sub = await C.AddSubPatientAsync(Pat);
        var r = await RecCreate(new ReceptionistCreateLabRequestDto { PatientId = sub.PatientId, LabItems = Items(Svc1) });
        r.PatientId.ShouldBe(sub.PatientId);
    }

    [Fact]
    public async Task Receptionist_duplicate_item_is_refused_and_nothing_is_saved()
    {
        await ShouldFailWith("LabRequest.DuplicateItem", () => RecCreate(RecDto(Svc1, Svc1)));
        await using var db = Db();
        (await db.LabRequests.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public Task Receptionist_ServicePrice_NotFound() =>
        ShouldFailWith("ServicePrice.NotFound", () => RecCreate(RecDto(999_999)));

    [Fact]
    public Task Receptionist_Patient_NotFound() =>
        ShouldFailWith("Patient.NotFound", () => RecCreate(new ReceptionistCreateLabRequestDto { PatientId = 999_999, LabItems = Items(Svc1) }));

    [Fact]
    public async Task Receptionist_Patient_NotFound_for_an_inactive_patient()
    {
        var gone = await C.AddPatientAsync(active: false);
        await ShouldFailWith("Patient.NotFound", () => RecCreate(new ReceptionistCreateLabRequestDto { PatientId = gone.PatientId, LabItems = Items(Svc1) }));
    }

    [Fact]
    public Task Receptionist_NotFound_when_the_caller_is_a_doctor() =>
        ShouldFailWith("Receptionist.NotFound", () => RecCreate(RecDto(Svc1), Doc.UserId));

    [Fact]
    public Task Receptionist_NotFound_for_an_unknown_user() =>
        ShouldFailWith("Receptionist.NotFound", () => RecCreate(RecDto(Svc1), 999_999));

    [Fact]
    public async Task Receptionist_NotFound_for_an_inactive_receptionist()
    {
        var inactive = await C.AddReceptionistAsync(active: false);
        await ShouldFailWith("Receptionist.NotFound", () => RecCreate(RecDto(Svc1), inactive));
    }

    [Fact]
    public Task Receptionist_Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => NewLabService().ReceptionistCreateLabRequestAsync(999_999, Rec, RecDto(Svc1)));
}
