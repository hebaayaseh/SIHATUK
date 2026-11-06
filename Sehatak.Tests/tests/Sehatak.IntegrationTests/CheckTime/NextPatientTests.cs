using Shouldly;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Domain.Enums;
using Sehatak.IntegrationTests.Infrastructure;

namespace Sehatak.IntegrationTests.CheckTime;

[Collection(SqlServerCollection.Name)]
public class NextPatientTests(SqlServerFixture fx) : CheckTimeTestBase(fx)
{
    [Fact]
    public async Task Next_patient_starts_the_visit_now()
    {
        var id = await AddTodayAsync(checkedIn: true);
        var status = await NewSvc().NextPatient(C.Id, Reception, id);

        status.ShouldBe("InProgress");
        var a = await LoadAsync(id);
        a.appointmentStatus.ShouldBe(AppointmentStatus.InProgress);
        ShouldBeNowUtc(a.actualStartTime!.Value);
        a.actualEndTime.ShouldBeNull();
        (await CountAuditAsync("NextPatient", id)).ShouldBe(1);
    }

    [Theory]
    [InlineData(AppointmentStatus.Pending)]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.InProgress)]
    public async Task Only_confirmed_appointments_can_be_started(AppointmentStatus status)
    {
        var id = await AddTodayAsync(status, checkedIn: true);
        await ShouldFailWith("Appointment.NotFound", () => NewSvc().NextPatient(C.Id, Reception, id));
    }

    [Fact]
    public async Task A_confirmed_appointment_that_already_has_a_start_time_is_AlreadyStarted()
    {
        var id = await AddTodayAsync(checkedIn: true, start: DateTime.UtcNow.AddMinutes(-5));
        await ShouldFailWith("Appointment.AlreadyStarted", () => NewSvc().NextPatient(C.Id, Reception, id));
    }

    [Fact]
    public async Task Inactive_user_and_suspended_center_are_refused()
    {
        var id = await AddTodayAsync(checkedIn: true);
        var off = await C.AddReceptionistAsync(active: false);
        await ShouldFailWith("User.NotFound", () => NewSvc().NextPatient(C.Id, off, id));

        await SuspendCenterAsync();
        await ShouldFailWith("Center.NotFound", () => NewSvc().NextPatient(C.Id, Reception, id));
    }

    // ------------------------------------------------------------------ known bugs

    /// <summary>Check-in is optional by design (the receptionist may not notice the patient arrived): the visit must start anyway.</summary>
    [Fact]
    public async Task A_patient_without_check_in_can_still_be_called()
    {
        var id = await AddTodayAsync(checkedIn: false);
        (await NewSvc().NextPatient(C.Id, Reception, id)).ShouldBe("InProgress");

        var a = await LoadAsync(id);
        a.appointmentStatus.ShouldBe(AppointmentStatus.InProgress);
        a.CheckInTime.ShouldBeNull();                 // stays empty: no fake arrival time is invented
    }

    /// <summary>An old "Confirmed" that nobody closed (or a future one) can be started today.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Only_todays_appointments_can_be_started()
    {
        var old = await AddTodayAsync(checkedIn: true, date: Today.AddDays(-2));
        await ShouldFailWith("Appointment.NotToday", () => NewSvc().NextPatient(C.Id, Reception, old));

        var future = await AddTodayAsync(checkedIn: true, date: Date);
        await ShouldFailWith("Appointment.NotToday", () => NewSvc().NextPatient(C.Id, Reception, future));
    }

    /// <summary>One doctor can't see two patients at once; starting a second one corrupts the waiting-time numbers.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_doctor_with_a_visit_in_progress_cannot_get_a_second_one()
    {
        await AddTodayAsync(AppointmentStatus.InProgress, checkedIn: true, start: DateTime.UtcNow.AddMinutes(-3));
        var next = await AddTodayAsync(checkedIn: true);
        await ShouldFailWith("Doctor.Busy", () => NewSvc().NextPatient(C.Id, Reception, next));
    }

    /// <summary>A different doctor is free, so the rule above must not block them.</summary>
    [Fact]
    public async Task Another_doctor_can_start_while_this_one_is_busy()
    {
        var other = await C.AddDoctorAsync();
        await AddTodayAsync(AppointmentStatus.InProgress, checkedIn: true, start: DateTime.UtcNow.AddMinutes(-3));
        var id = await AddTodayAsync(doctor: other, checkedIn: true);
        (await NewSvc().NextPatient(C.Id, Reception, id)).ShouldBe("InProgress");
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Two_simultaneous_starts_only_one_wins()
    {
        var id = await AddTodayAsync(checkedIn: true);
        var gate = new TaskCompletionSource();
        async Task<bool> Try()
        {
            await gate.Task;
            try { await NewSvc().NextPatient(C.Id, Reception, id); return true; }
            catch (BusinessException) { return false; }
        }
        var tasks = Enumerable.Range(0, 4).Select(_ => Task.Run(Try)).ToArray();
        gate.SetResult();
        (await Task.WhenAll(tasks)).Count(ok => ok).ShouldBe(1);
        (await CountAuditAsync("NextPatient", id)).ShouldBe(1);
    }
}
