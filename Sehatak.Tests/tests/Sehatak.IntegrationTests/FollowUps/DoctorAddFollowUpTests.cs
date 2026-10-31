using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.FollowUpDto;
using Sehatak.Domain.Enums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.FollowUps;

/// <summary>The doctor opens a follow-up (a free return visit allowed until a date) for one of his appointments.</summary>
[Collection(SqlServerCollection.Name)]
public class DoctorAddFollowUpTests(SqlServerFixture fx) : FollowUpTestBase(fx)
{
    private DoctorAddFollowUpRequestDto Req(int? appointmentId = null, int? patientId = null, DateOnly? date = null, bool noDate = false) => new()
    {
        OriginalAppointmentId = appointmentId ?? Appt, PatientId = patientId ?? Pat.PatientId,
        AllowFollowUpDate = noDate ? null : date ?? FollowUpDate
    };

    private Task<FollowUpResponseDto> Add(DoctorAddFollowUpRequestDto req, int? userId = null) =>
        NewFollowService().DoctorAddFollowUpAsync(C.Id, userId ?? Doc.UserId, req);

    [Fact]
    public async Task Doctor_creates_a_Pending_follow_up_notifies_the_patient_and_audits()
    {
        var r = await Add(Req());

        r.Status.ShouldBe(FollowUpStatus.Pending);
        r.OriginalAppointmentId.ShouldBe(Appt);
        r.PatientId.ShouldBe(Pat.PatientId);
        r.DoctorId.ShouldBe(Doc.DoctorId);
        r.AllowFollowUpDate.ShouldBe(FollowUpDate);
        r.PatientName.ShouldNotBeNullOrWhiteSpace();
        r.DoctorName.ShouldNotBeNullOrWhiteSpace();
        r.ReceptionistId.ShouldBeNull();

        var f = await LoadFollowUpAsync(r.Id);
        f.Status.ShouldBe(FollowUpStatus.Pending);
        f.ScheduledAppointmentId.ShouldBeNull();
        ShouldBeNowUtc(f.CreatedAt);
        ShouldBeNowUtc(f.UpdatedAt);

        var notes = await NotificationsOfAsync(Pat.UserId);
        notes.Count.ShouldBe(1);
        notes[0].Type.ShouldBe(NotificationType.Appointment);
        ShouldBeNowUtc(notes[0].CreatedAt);
        (await CountAuditAsync("AddFollowUp", r.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task A_follow_up_without_a_date_is_allowed()
    {
        var r = await Add(Req(noDate: true));
        r.AllowFollowUpDate.ShouldBeNull();
        (await LoadFollowUpAsync(r.Id)).AllowFollowUpDate.ShouldBeNull();
    }

    [Fact]
    public async Task Today_is_a_valid_date()
    {
        var r = await Add(Req(date: ClinicClock.Today));
        r.AllowFollowUpDate.ShouldBe(ClinicClock.Today);
    }

    [Fact]
    public async Task Date_Invalid_for_a_date_in_the_past_and_nothing_is_saved()
    {
        await ShouldFailWith("Date.Invalid", () => Add(Req(date: ClinicClock.Today.AddDays(-1))));

        await using var db = Db();
        (await db.FollowUps.CountAsync()).ShouldBe(0);
        (await db.Notifications.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task For_a_child_the_notification_goes_to_the_parent()
    {
        var sub = await C.AddSubPatientAsync(Pat);
        var appt = await C.AddAppointmentAsync(Doc.DoctorId, sub.PatientId, Date, S0930);

        var r = await Add(Req(appt, sub.PatientId));

        r.PatientId.ShouldBe(sub.PatientId);
        r.PatientName.ShouldNotBeNullOrWhiteSpace();
        (await NotificationsOfAsync(Pat.UserId)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task AlreadyExists_while_a_Pending_follow_up_exists_for_the_appointment()
    {
        await Add(Req());
        await ShouldFailWith("FollowUp.AlreadyExists", () => Add(Req()));
        await using var db = Db();
        (await db.FollowUps.CountAsync()).ShouldBe(1);
    }

    [Theory]
    [InlineData(FollowUpStatus.Booked)]
    [InlineData(FollowUpStatus.Completed)]
    [InlineData(FollowUpStatus.Cancelled)]
    public async Task A_new_follow_up_is_allowed_once_the_previous_one_is_not_Pending(FollowUpStatus previous)
    {
        await SeedFollowUpAsync(status: previous, originalAppointmentId: Appt);
        var r = await Add(Req());
        r.Status.ShouldBe(FollowUpStatus.Pending);
    }

    /// <summary>The duplicate check is read-then-insert with no unique index, so a double click / two devices create two Pending follow-ups.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Parallel_adds_create_only_one_follow_up()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            try { await Add(Req()); return true; } catch { return false; }
        }));

        results.Count(r => r).ShouldBe(1);
        await using var db = Db();
        (await db.FollowUps.CountAsync(f => f.OriginalAppointmentId == Appt && f.Status == FollowUpStatus.Pending)).ShouldBe(1);
        (await db.Notifications.CountAsync(n => n.UserId == Pat.UserId)).ShouldBe(1);
    }

    // ------------------------------------------------------------------ which appointments qualify

    [Theory]
    [InlineData(AppointmentStatus.Pending)]
    [InlineData(AppointmentStatus.Cancelled)]
    public async Task Appointment_NotFound_for_an_appointment_that_is_not_confirmed(AppointmentStatus status)
    {
        await SetAppointmentStatusAsync(Appt, status);
        await ShouldFailWith("Appointment.NotFound", () => Add(Req()));
    }

    /// <summary>
    /// Only a Confirmed appointment qualifies, but the visit moves Confirmed -> InProgress (doctor presses "next patient") ->
    /// Completed. So the doctor can open a follow-up only BEFORE the patient walks in; during or after the visit he gets
    /// Appointment.NotFound. Booking a follow-up (AppointmentService) expects the original visit to be done.
    /// </summary>
    [Theory]
    [Trait("Category", "KnownBug")]
    [InlineData(AppointmentStatus.InProgress)]
    [InlineData(AppointmentStatus.Completed)]
    public async Task A_follow_up_can_be_added_during_or_after_the_visit(AppointmentStatus status)
    {
        await SetAppointmentStatusAsync(Appt, status);
        var r = await Add(Req());
        r.Status.ShouldBe(FollowUpStatus.Pending);
    }

    [Fact]
    public async Task Appointment_NotFound_for_another_doctors_appointment()
    {
        var doc2 = await C.AddDoctorAsync();
        await ShouldFailWith("Appointment.NotFound", () => Add(Req(), doc2.UserId));
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
    public Task Doctor_NotFound_when_the_caller_is_not_a_doctor() =>
        ShouldFailWith("Doctor.NotFound", () => Add(Req(), Rec));

    [Fact]
    public async Task Doctor_NotFound_for_an_inactive_doctor()
    {
        var inactive = await C.AddDoctorAsync(active: false);
        await ShouldFailWith("Doctor.NotFound", () => Add(Req(), inactive.UserId));
    }

    [Fact]
    public Task Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => NewFollowService().DoctorAddFollowUpAsync(999_999, Doc.UserId, Req()));
}
