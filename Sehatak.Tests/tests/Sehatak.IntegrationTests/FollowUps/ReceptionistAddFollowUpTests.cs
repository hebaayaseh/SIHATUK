using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.FollowUpDto;
using Sehatak.Domain.Enums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.FollowUps;

/// <summary>The receptionist opens a follow-up on behalf of a doctor (the doctor id comes in the request).</summary>
[Collection(SqlServerCollection.Name)]
public class ReceptionistAddFollowUpTests(SqlServerFixture fx) : FollowUpTestBase(fx)
{
    private ReceptionistAddFollowUpRequestDto Req(int? appointmentId = null, int? patientId = null, int? doctorId = null,
        DateOnly? date = null, bool noDate = false) => new()
    {
        OriginalAppointmentId = appointmentId ?? Appt, PatientId = patientId ?? Pat.PatientId,
        DoctorId = doctorId ?? Doc.DoctorId, AllowFollowUpDate = noDate ? null : date ?? FollowUpDate
    };

    private Task<FollowUpResponseDto> Add(ReceptionistAddFollowUpRequestDto req, int? userId = null) =>
        NewFollowService().ReceptionistAddFollowUpAsync(C.Id, userId ?? Rec, req);

    [Fact]
    public async Task Receptionist_creates_a_Pending_follow_up_for_the_doctor()
    {
        var r = await Add(Req());

        r.Status.ShouldBe(FollowUpStatus.Pending);
        r.DoctorId.ShouldBe(Doc.DoctorId);
        r.ReceptionistId.ShouldBe(Rec);
        r.OriginalAppointmentId.ShouldBe(Appt);
        r.AllowFollowUpDate.ShouldBe(FollowUpDate);
        r.PatientName.ShouldNotBeNullOrWhiteSpace();
        r.DoctorName.ShouldNotBeNullOrWhiteSpace();

        var f = await LoadFollowUpAsync(r.Id);
        f.ReceptionistId.ShouldBe(Rec);
        f.DoctorId.ShouldBe(Doc.DoctorId);

        var notes = await NotificationsOfAsync(Pat.UserId);
        notes.Count.ShouldBe(1);
        notes[0].Type.ShouldBe(NotificationType.Appointment);
        (await CountAuditAsync("AddFollowUp", r.Id)).ShouldBe(1);
    }

    /// <summary>ReceptionistAddFollowUpAsync uses ClinicClock.Now (local time) for CreatedAt / UpdatedAt / the notification. Everything else in the system is stored in UTC.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Timestamps_are_stored_in_UTC()
    {
        var r = await Add(Req());

        var f = await LoadFollowUpAsync(r.Id);
        ShouldBeNowUtc(f.CreatedAt);
        ShouldBeNowUtc(f.UpdatedAt);
        ShouldBeNowUtc((await NotificationsOfAsync(Pat.UserId)).Single().CreatedAt);
    }

    [Fact]
    public async Task A_follow_up_without_a_date_is_allowed()
    {
        var r = await Add(Req(noDate: true));
        r.AllowFollowUpDate.ShouldBeNull();
    }

    [Fact]
    public async Task Date_Invalid_for_a_date_in_the_past_and_nothing_is_saved()
    {
        await ShouldFailWith("Date.Invalid", () => Add(Req(date: ClinicClock.Today.AddDays(-1))));
        await using var db = Db();
        (await db.FollowUps.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task For_a_child_the_notification_goes_to_the_parent()
    {
        var sub = await C.AddSubPatientAsync(Pat);
        var appt = await C.AddAppointmentAsync(Doc.DoctorId, sub.PatientId, Date, S0930);

        var r = await Add(Req(appt, sub.PatientId));

        r.PatientId.ShouldBe(sub.PatientId);
        (await NotificationsOfAsync(Pat.UserId)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task AlreadyExists_while_a_Pending_follow_up_exists_for_the_appointment()
    {
        await Add(Req());
        await ShouldFailWith("FollowUp.AlreadyExists", () => Add(Req()));
    }

    [Fact]
    public async Task A_doctors_pending_follow_up_also_blocks_the_receptionist()
    {
        await SeedFollowUpAsync(originalAppointmentId: Appt);
        await ShouldFailWith("FollowUp.AlreadyExists", () => Add(Req()));
    }

    [Theory]
    [InlineData(AppointmentStatus.Pending)]
    [InlineData(AppointmentStatus.Cancelled)]
    public async Task Appointment_NotFound_for_an_appointment_that_is_not_confirmed(AppointmentStatus status)
    {
        await SetAppointmentStatusAsync(Appt, status);
        await ShouldFailWith("Appointment.NotFound", () => Add(Req()));
    }

    /// <summary>Same rule as the doctor version: InProgress / Completed visits are refused today.</summary>
    [Theory]
    [Trait("Category", "KnownBug")]
    [InlineData(AppointmentStatus.InProgress)]
    [InlineData(AppointmentStatus.Completed)]
    public async Task A_follow_up_can_be_added_during_or_after_the_visit(AppointmentStatus status)
    {
        await SetAppointmentStatusAsync(Appt, status);
        (await Add(Req())).Status.ShouldBe(FollowUpStatus.Pending);
    }

    [Fact]
    public async Task Appointment_NotFound_when_the_doctor_id_does_not_match_the_appointment()
    {
        var doc2 = await C.AddDoctorAsync();
        await ShouldFailWith("Appointment.NotFound", () => Add(Req(doctorId: doc2.DoctorId)));
    }

    [Fact]
    public async Task Appointment_NotFound_when_the_patient_does_not_match()
    {
        var other = await C.AddPatientAsync();
        await ShouldFailWith("Appointment.NotFound", () => Add(Req(patientId: other.PatientId)));
    }

    [Fact]
    public Task Appointment_NotFound_for_an_unknown_appointment() =>
        ShouldFailWith("Appointment.NotFound", () => Add(Req(999_999)));

    [Fact]
    public async Task Doctor_NotFound_when_the_appointments_doctor_is_inactive()
    {
        var inactive = await C.AddDoctorAsync(active: false);
        var appt = await C.AddAppointmentAsync(inactive.DoctorId, Pat.PatientId, Date, S1000);
        await ShouldFailWith("Doctor.NotFound", () => Add(Req(appt, doctorId: inactive.DoctorId)));
    }

    [Fact]
    public Task Receptionist_NotFound_for_an_unknown_user() =>
        ShouldFailWith("Receptionist.NotFound", () => Add(Req(), 999_999));

    [Fact]
    public async Task Receptionist_NotFound_for_an_inactive_user()
    {
        var inactive = await C.AddReceptionistAsync(active: false);
        await ShouldFailWith("Receptionist.NotFound", () => Add(Req(), inactive));
    }

    [Fact]
    public Task Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => NewFollowService().ReceptionistAddFollowUpAsync(999_999, Rec, Req()));
}
