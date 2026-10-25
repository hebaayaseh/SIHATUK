using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Domain.Enums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Appointments;

/// <summary>AppointmentService.JoinWaitListAsync (explicit join, as opposed to the automatic one when a day is full).</summary>
[Collection(SqlServerCollection.Name)]
public class JoinWaitListTests(SqlServerFixture fx) : AppointmentTestBase(fx)
{
    private Task<string> Join(int userId, DateOnly? date = null, int? sub = null, int? doctorId = null, int? centerId = null) =>
        C.NewAppointmentService().JoinWaitListAsync(centerId ?? C.Id, doctorId ?? Doc.DoctorId, userId, date ?? Date, sub);

    [Fact]
    public async Task Joins_the_waitlist_and_writes_an_audit_entry()
    {
        var msg = await Join(Pat.UserId);

        msg.ShouldNotBeNullOrWhiteSpace();
        await using var db = Db();
        var w = await db.Waitlists.SingleAsync();
        w.PatientId.ShouldBe(Pat.PatientId);
        w.DoctorId.ShouldBe(Doc.DoctorId);
        w.PreferredDate.ShouldBe(Date);
        w.Status.ShouldBe(WaitlistStatus.Waiting);
        Math.Abs((w.CreatedAt - DateTime.UtcNow).TotalMinutes).ShouldBeLessThan(5);   // stored as UTC
        (await db.AuditLogs.CountAsync(a => a.Action == "JoinWaitList" && a.EntityId == w.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task A_sub_patient_can_join()
    {
        var sub = await C.AddSubPatientAsync(Pat);

        await Join(Pat.UserId, sub: sub.PatientId);

        await using var db = Db();
        (await db.Waitlists.SingleAsync()).PatientId.ShouldBe(sub.PatientId);
    }

    [Fact]
    public async Task Waitlist_AlreadyJoined_for_the_same_patient_doctor_and_day()
    {
        await Join(Pat.UserId);

        await ShouldFailWith("Waitlist.AlreadyJoined", () => Join(Pat.UserId));

        await using var db = Db();
        (await db.Waitlists.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task The_same_patient_may_wait_for_a_different_day()
    {
        await FillDayAsync(Date.AddDays(7));
        await Join(Pat.UserId);
        await Join(Pat.UserId, Date.AddDays(7));

        await using var db = Db();
        (await db.Waitlists.CountAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task The_parent_and_a_child_can_both_wait_for_the_same_day()
    {
        var sub = await C.AddSubPatientAsync(Pat);
        await Join(Pat.UserId);
        await Join(Pat.UserId, sub: sub.PatientId);

        await using var db = Db();
        (await db.Waitlists.CountAsync()).ShouldBe(2);
    }

    [Theory]
    [InlineData(WaitlistStatus.Entered)]
    [InlineData(WaitlistStatus.Cancelled)]
    [InlineData(WaitlistStatus.Exited)]
    public async Task Can_join_again_once_the_previous_entry_is_no_longer_waiting(WaitlistStatus old)
    {
        await Join(Pat.UserId);
        await using (var db = Db())
        {
            (await db.Waitlists.SingleAsync()).Status = old;
            await db.SaveChangesAsync();
        }

        await Join(Pat.UserId);

        await using var check = Db();
        (await check.Waitlists.CountAsync(w => w.Status == WaitlistStatus.Waiting)).ShouldBe(1);
    }

    // ------------------------------------------------------------------ business errors

    [Fact]
    public Task Date_Invalid_for_the_past() =>
        ShouldFailWith("Date.Invalid", () => Join(Pat.UserId, ClinicClock.Today.AddDays(-2)));

    [Fact]
    public Task Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => Join(Pat.UserId, centerId: 999_999));

    [Fact]
    public Task Doctor_NotFound() =>
        ShouldFailWith("Doctor.NotFound", () => Join(Pat.UserId, doctorId: 999_999));

    [Fact]
    public Task Patient_NotFound() =>
        ShouldFailWith("Patient.NotFound", () => Join(999_999));

    [Fact]
    public async Task SubPatient_NotFoundOrNotOwned_for_someone_elses_child()
    {
        var stranger = await C.AddPatientAsync();
        var child = await C.AddSubPatientAsync(stranger);

        await ShouldFailWith("SubPatient.NotFoundOrNotOwned", () => Join(Pat.UserId, sub: child.PatientId));
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await FillDayAsync(Date);
    }

    private async Task FillDayAsync(DateOnly day)
    {
        var filler = await C.AddPatientAsync();
        foreach (var s in new[] { S0900, S0930, S1000, S1030 })
            await C.AddAppointmentAsync(Doc.DoctorId, filler.PatientId, day, s);
    }

    [Fact]
    public async Task Waitlist_SlotsAvailable_when_the_day_has_free_slots()
    {
        await ShouldFailWith("Waitlist.SlotsAvailable", () => Join(Pat.UserId, Date.AddDays(14)));

        await using var db = Db();
        (await db.Waitlists.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Waitlist_SlotsAvailable_even_when_only_one_slot_is_left()
    {
        var day = Date.AddDays(14);
        var filler = await C.AddPatientAsync();
        foreach (var s in new[] { S0900, S0930, S1000 })
            await C.AddAppointmentAsync(Doc.DoctorId, filler.PatientId, day, s);

        await ShouldFailWith("Waitlist.SlotsAvailable", () => Join(Pat.UserId, day));
    }

    [Fact]
    public Task Schedule_NotFound_when_the_doctor_does_not_work_that_day() =>
        ShouldFailWith("Schedule.NotFound", () => Join(Pat.UserId, TestCenter.FutureDate(DayOfWeek.Tuesday)));
}
