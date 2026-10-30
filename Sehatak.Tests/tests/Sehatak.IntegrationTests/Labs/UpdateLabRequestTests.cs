using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.LabDto;
using Sehatak.Domain.Enums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Labs;

/// <summary>Editing a still-Pending request (add / remove tests, change the note): doctor and receptionist versions.</summary>
[Collection(SqlServerCollection.Name)]
public class UpdateLabRequestTests(SqlServerFixture fx) : LabTestBase(fx)
{
    private static List<LabRequestItemsSummaryDto> Items(params int[] ids) => ids.Select(i => new LabRequestItemsSummaryDto { Id = i }).ToList();

    private Task<LabRequestResponseDto> DoctorUpdate(int labId, string? note = null, int[]? add = null, int[]? remove = null,
        int? userId = null, int? patientId = null, int? appointmentId = null) =>
        NewLabService().UpdateLabRequestAsync(C.Id, userId ?? Doc.UserId, new UpdateLabRequestDto
        {
            LabRequestId = labId, PatientId = patientId ?? Pat.PatientId, AppointmentId = appointmentId ?? Appt,
            Note = note, AddLabItems = add == null ? null : Items(add), RemoveLabItems = remove?.ToList()
        });

    private Task<ReceptionistLabRequestReponseDto> RecUpdate(int labId, string? note = null, int[]? add = null, int[]? remove = null,
        int? userId = null, int? patientId = null) =>
        NewLabService().ReceptionistUpdateLabRequestAsync(C.Id, userId ?? Rec, new ReceptionistUpdateLabRequestDto
        {
            LabRequestId = labId, PatientId = patientId ?? Pat.PatientId,
            Note = note, AddLabItems = add == null ? null : Items(add), RemoveLabItems = remove?.ToList()
        });

    // ------------------------------------------------------------------ Doctor

    [Fact]
    public async Task Doctor_adds_a_test_and_the_total_follows()
    {
        var (id, _) = await SeedLabAsync();            // Svc1 (10) + Svc2 (20)

        var r = await DoctorUpdate(id, add: [Svc3]);

        r.TotalPrice.ShouldBe(60m);
        r.LabItems!.Count.ShouldBe(3);
        r.LabItems!.Select(i => i.ItemId).ShouldAllBe(x => x > 0);   // here the ids are real (read back from the db)
        ShouldBeNowUtc((await LoadLabAsync(id)).UpdatedAt);
        (await CountAuditAsync("UpdateLabRequest", id)).ShouldBe(1);
    }

    [Fact]
    public async Task Doctor_removes_a_test()
    {
        var (id, items) = await SeedLabAsync();

        var r = await DoctorUpdate(id, remove: [items[0]]);

        r.LabItems!.Count.ShouldBe(1);
        r.TotalPrice.ShouldBe(20m);
        (await LoadLabAsync(id)).Items.Select(i => i.Id).ShouldBe(new[] { items[1] });
    }

    [Fact]
    public async Task Doctor_can_remove_and_re_add_the_same_test_in_one_call()
    {
        var (id, items) = await SeedLabAsync();

        var r = await DoctorUpdate(id, add: [Svc1], remove: [items[0]]);

        r.LabItems!.Count.ShouldBe(2);
        r.TotalPrice.ShouldBe(30m);
    }

    [Fact]
    public async Task Doctor_changes_only_the_note()
    {
        var (id, _) = await SeedLabAsync();

        var r = await DoctorUpdate(id, note: "new note");

        r.Note.ShouldBe("new note");
        r.LabItems!.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_null_note_keeps_the_old_note()
    {
        var (id, _) = await SeedLabAsync();
        await using (var db = Db()) { (await db.LabRequests.SingleAsync()).Notes = "keep me"; await db.SaveChangesAsync(); }

        (await DoctorUpdate(id, note: null, add: [Svc3])).Note.ShouldBe("keep me");
    }

    [Fact]
    public async Task Removing_an_id_that_is_not_in_the_request_is_ignored()
    {
        var (id, _) = await SeedLabAsync();
        var (otherId, otherItems) = await SeedLabAsync();

        await DoctorUpdate(id, remove: [otherItems[0]]);

        (await LoadLabAsync(otherId)).Items.Count.ShouldBe(2);   // the other request is untouched
        (await LoadLabAsync(id)).Items.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Duplicate_test_is_refused_and_nothing_changes()
    {
        var (id, items) = await SeedLabAsync();

        // Svc1 is already there; the removal in the same call must not be applied either.
        await ShouldFailWith("LabRequest.DuplicateItem", () => DoctorUpdate(id, add: [Svc1], remove: [items[1]], note: "x"));

        var lab = await LoadLabAsync(id);
        lab.Items.Count.ShouldBe(2);
        lab.Notes.ShouldNotBe("x");
    }

    [Fact]
    public async Task ServicePrice_NotFound_for_an_unknown_test()
    {
        var (id, _) = await SeedLabAsync();
        await ShouldFailWith("ServicePrice.NotFound", () => DoctorUpdate(id, add: [999_999]));
    }

    [Theory]
    [InlineData(LabRequestStatus.Collected)]
    [InlineData(LabRequestStatus.Processing)]
    [InlineData(LabRequestStatus.Completed)]
    [InlineData(LabRequestStatus.Cancelled)]
    public async Task Only_a_Pending_request_can_be_edited(LabRequestStatus status)
    {
        var (id, _) = await SeedLabAsync(status);
        await ShouldFailWith("LabRequest.NotFound", () => DoctorUpdate(id, add: [Svc3]));
        (await LoadLabAsync(id)).Items.Count.ShouldBe(2);
    }

    [Fact]
    public async Task LabRequest_NotFound_when_it_belongs_to_another_appointment()
    {
        var other = await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S1000);
        var (id, _) = await SeedLabAsync(appointmentId: other);
        await ShouldFailWith("LabRequest.NotFound", () => DoctorUpdate(id, add: [Svc3]));   // the call points at Appt, not 'other'
    }

    [Fact]
    public Task LabRequest_NotFound_for_an_unknown_request() =>
        ShouldFailWith("LabRequest.NotFound", () => DoctorUpdate(999_999, add: [Svc3]));

    [Fact]
    public async Task Appointment_NotFound_for_a_cancelled_appointment()
    {
        var (id, _) = await SeedLabAsync();
        await using (var db = Db()) { (await db.Appointments.SingleAsync(a => a.Id == Appt)).appointmentStatus = AppointmentStatus.Cancelled; await db.SaveChangesAsync(); }
        await ShouldFailWith("Appointment.NotFound", () => DoctorUpdate(id, add: [Svc3]));
    }

    [Fact]
    public async Task Appointment_NotFound_for_another_doctor()
    {
        var (id, _) = await SeedLabAsync();
        var doc2 = await C.AddDoctorAsync();
        await ShouldFailWith("Appointment.NotFound", () => DoctorUpdate(id, add: [Svc3], userId: doc2.UserId));
    }

    [Fact]
    public Task Patient_NotFound() =>
        ShouldFailWith("Patient.NotFound", () => DoctorUpdate(1, patientId: 999_999));

    [Fact]
    public Task Doctor_NotFound() =>
        ShouldFailWith("Doctor.NotFound", () => DoctorUpdate(1, userId: Rec));

    [Fact]
    public Task Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => NewLabService().UpdateLabRequestAsync(999_999, Doc.UserId, new UpdateLabRequestDto()));

    // ------------------------------------------------------------------ Receptionist

    [Fact]
    public async Task Receptionist_adds_and_removes_tests_on_a_doctors_request()
    {
        var (id, items) = await SeedLabAsync();

        var r = await RecUpdate(id, note: "front desk", add: [Svc3], remove: [items[0]]);

        r.TotalPrice.ShouldBe(50m);          // Svc2 (20) + Svc3 (30)
        r.Note.ShouldBe("front desk");
        r.LabItems!.Select(i => i.ServicePriceId).ShouldBe(new[] { Svc2, Svc3 }, ignoreOrder: true);
        (await CountAuditAsync("ReceptionistUpdateLabRequest", id)).ShouldBe(1);
    }

    [Fact]
    public async Task Receptionist_duplicate_test_is_refused_and_nothing_changes()
    {
        var (id, items) = await SeedLabAsync();
        await ShouldFailWith("LabRequest.DuplicateItem", () => RecUpdate(id, add: [Svc2], remove: [items[0]]));
        (await LoadLabAsync(id)).Items.Count.ShouldBe(2);
    }

    [Theory]
    [InlineData(LabRequestStatus.Collected)]
    [InlineData(LabRequestStatus.Processing)]
    [InlineData(LabRequestStatus.Completed)]
    [InlineData(LabRequestStatus.Cancelled)]
    public async Task Receptionist_can_only_edit_a_Pending_request(LabRequestStatus status)
    {
        var (id, _) = await SeedLabAsync(status);
        await ShouldFailWith("LabRequest.NotFound", () => RecUpdate(id, add: [Svc3]));
    }

    [Fact]
    public async Task Receptionist_LabRequest_NotFound_when_the_patient_does_not_match()
    {
        var other = await C.AddPatientAsync();
        var (id, _) = await SeedLabAsync();
        await ShouldFailWith("LabRequest.NotFound", () => RecUpdate(id, add: [Svc3], patientId: other.PatientId));
    }

    [Fact]
    public async Task Receptionist_ServicePrice_NotFound()
    {
        var (id, _) = await SeedLabAsync();
        await ShouldFailWith("ServicePrice.NotFound", () => RecUpdate(id, add: [999_999]));
    }

    [Fact]
    public Task Receptionist_Patient_NotFound() =>
        ShouldFailWith("Patient.NotFound", () => RecUpdate(1, patientId: 999_999));

    [Fact]
    public Task Receptionist_NotFound_when_the_caller_is_a_doctor() =>
        ShouldFailWith("Receptionist.NotFound", () => RecUpdate(1, userId: Doc.UserId));

    [Fact]
    public Task Receptionist_Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => NewLabService().ReceptionistUpdateLabRequestAsync(999_999, Rec, new ReceptionistUpdateLabRequestDto()));
}
