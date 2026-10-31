using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.FollowUpDto;
using Sehatak.Domain.Enums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.FollowUps;

/// <summary>Changing the "allowed until" date of a Pending follow-up (doctor: his own; receptionist: any doctor's).</summary>
[Collection(SqlServerCollection.Name)]
public class UpdateFollowUpTests(SqlServerFixture fx) : FollowUpTestBase(fx)
{
    private DateOnly NewDate => FollowUpDate.AddDays(5);

    private UpdateFollowUpRequestDto Req(int followUpId, int? patientId = null, DateOnly? date = null, bool noDate = false) => new()
    {
        FollowUpId = followUpId, PatientId = patientId ?? Pat.PatientId, AllowFollowUpDate = noDate ? null : date ?? NewDate
    };

    private Task<FollowUpResponseDto> DoctorUpdate(UpdateFollowUpRequestDto req, int? userId = null) =>
        NewFollowService().DoctorUpdateFollowUpAsync(C.Id, userId ?? Doc.UserId, req);

    private Task<FollowUpResponseDto> RecUpdate(UpdateFollowUpRequestDto req, int? userId = null) =>
        NewFollowService().ReceptionistUpdateFollowUpAsync(C.Id, userId ?? Rec, req);

    // ------------------------------------------------------------------ Doctor

    [Fact]
    public async Task Doctor_changes_the_date_and_the_patient_is_told()
    {
        var id = await SeedFollowUpAsync();

        var r = await DoctorUpdate(Req(id));

        r.AllowFollowUpDate.ShouldBe(NewDate);
        r.Status.ShouldBe(FollowUpStatus.Pending);
        r.PatientName.ShouldNotBeNullOrWhiteSpace();
        r.DoctorName.ShouldNotBeNullOrWhiteSpace();

        (await LoadFollowUpAsync(id)).AllowFollowUpDate.ShouldBe(NewDate);
        var notes = await NotificationsOfAsync(Pat.UserId);
        notes.Count.ShouldBe(1);
        notes[0].Type.ShouldBe(NotificationType.Appointment);
        (await CountAuditAsync("UpdateFollowUp", id)).ShouldBe(1);
    }

    [Fact]
    public async Task The_notification_goes_to_the_parent_for_a_child()
    {
        var sub = await C.AddSubPatientAsync(Pat);
        var id = await SeedFollowUpAsync(patientId: sub.PatientId);

        await DoctorUpdate(Req(id, sub.PatientId));

        (await NotificationsOfAsync(Pat.UserId)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_date_can_be_cleared()
    {
        var id = await SeedFollowUpAsync();
        (await DoctorUpdate(Req(id, noDate: true))).AllowFollowUpDate.ShouldBeNull();
    }

    /// <summary>DoctorUpdateFollowUpAsync takes ClinicClock.Now (local time) for UpdatedAt and for the notification's CreatedAt.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Doctor_update_stores_UTC_timestamps()
    {
        var id = await SeedFollowUpAsync();
        await DoctorUpdate(Req(id));

        ShouldBeNowUtc((await LoadFollowUpAsync(id)).UpdatedAt);
        ShouldBeNowUtc((await NotificationsOfAsync(Pat.UserId)).Single().CreatedAt);
    }

    /// <summary>Add refuses a past date (Date.Invalid) but Update does not check the date at all.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Doctor_update_refuses_a_date_in_the_past()
    {
        var id = await SeedFollowUpAsync();
        await ShouldFailWith("Date.Invalid", () => DoctorUpdate(Req(id, date: ClinicClock.Today.AddDays(-3))));
        (await LoadFollowUpAsync(id)).AllowFollowUpDate.ShouldBe(FollowUpDate);
    }

    [Theory]
    [InlineData(FollowUpStatus.Booked)]
    [InlineData(FollowUpStatus.Completed)]
    [InlineData(FollowUpStatus.Cancelled)]
    public async Task CannotBeUpdated_unless_the_follow_up_is_Pending(FollowUpStatus status)
    {
        var id = await SeedFollowUpAsync(status: status);
        await ShouldFailWith("FollowUp.CannotBeUpdated", () => DoctorUpdate(Req(id)));
        (await LoadFollowUpAsync(id)).AllowFollowUpDate.ShouldBe(FollowUpDate);
    }

    [Fact]
    public async Task FollowUp_NotFound_for_another_doctors_follow_up()
    {
        var id = await SeedFollowUpAsync();
        var doc2 = await C.AddDoctorAsync();
        await ShouldFailWith("FollowUp.NotFound", () => DoctorUpdate(Req(id), doc2.UserId));
    }

    [Fact]
    public async Task FollowUp_NotFound_when_the_patient_does_not_match()
    {
        var id = await SeedFollowUpAsync();
        var other = await C.AddPatientAsync();
        await ShouldFailWith("FollowUp.NotFound", () => DoctorUpdate(Req(id, other.PatientId)));
    }

    [Fact]
    public Task FollowUp_NotFound_for_an_unknown_id() =>
        ShouldFailWith("FollowUp.NotFound", () => DoctorUpdate(Req(999_999)));

    [Fact]
    public Task Doctor_NotFound_when_the_caller_is_not_a_doctor() =>
        ShouldFailWith("Doctor.NotFound", () => DoctorUpdate(Req(1), Rec));

    [Fact]
    public Task Doctor_Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => NewFollowService().DoctorUpdateFollowUpAsync(999_999, Doc.UserId, Req(1)));

    // ------------------------------------------------------------------ Receptionist

    [Fact]
    public async Task Receptionist_changes_the_date_of_any_doctors_follow_up()
    {
        var doc2 = await C.AddDoctorAsync();
        var id = await SeedFollowUpAsync(doctorId: doc2.DoctorId);

        var r = await RecUpdate(Req(id));

        r.AllowFollowUpDate.ShouldBe(NewDate);
        r.DoctorId.ShouldBe(doc2.DoctorId);
        r.DoctorName.ShouldNotBeNullOrWhiteSpace();
        r.PatientName.ShouldNotBeNullOrWhiteSpace();
        ShouldBeNowUtc((await LoadFollowUpAsync(id)).UpdatedAt);
        (await NotificationsOfAsync(Pat.UserId)).Count.ShouldBe(1);
        (await CountAuditAsync("UpdateFollowUp", id)).ShouldBe(1);
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Receptionist_update_refuses_a_date_in_the_past()
    {
        var id = await SeedFollowUpAsync();
        await ShouldFailWith("Date.Invalid", () => RecUpdate(Req(id, date: ClinicClock.Today.AddDays(-3))));
    }

    [Theory]
    [InlineData(FollowUpStatus.Booked)]
    [InlineData(FollowUpStatus.Completed)]
    [InlineData(FollowUpStatus.Cancelled)]
    public async Task Receptionist_CannotBeUpdated_unless_Pending(FollowUpStatus status)
    {
        var id = await SeedFollowUpAsync(status: status);
        await ShouldFailWith("FollowUp.CannotBeUpdated", () => RecUpdate(Req(id)));
    }

    [Fact]
    public async Task Receptionist_FollowUp_NotFound_when_the_patient_does_not_match()
    {
        var id = await SeedFollowUpAsync();
        var other = await C.AddPatientAsync();
        await ShouldFailWith("FollowUp.NotFound", () => RecUpdate(Req(id, other.PatientId)));
    }

    [Fact]
    public Task Receptionist_FollowUp_NotFound_for_an_unknown_id() =>
        ShouldFailWith("FollowUp.NotFound", () => RecUpdate(Req(999_999)));

    [Fact]
    public Task Receptionist_NotFound_for_an_unknown_user() =>
        ShouldFailWith("Receptionist.NotFound", () => RecUpdate(Req(1), 999_999));

    [Fact]
    public Task Receptionist_Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => NewFollowService().ReceptionistUpdateFollowUpAsync(999_999, Rec, Req(1)));
}
