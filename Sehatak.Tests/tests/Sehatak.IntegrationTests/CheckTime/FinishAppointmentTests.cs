using Shouldly;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Domain.Enums;
using Sehatak.IntegrationTests.Infrastructure;

namespace Sehatak.IntegrationTests.CheckTime;

[Collection(SqlServerCollection.Name)]
public class FinishAppointmentTests(SqlServerFixture fx) : CheckTimeTestBase(fx)
{
    private Task<int> InProgress(DoctorInfo? d = null) =>
        AddTodayAsync(AppointmentStatus.InProgress, d, checkedIn: true, start: DateTime.UtcNow.AddMinutes(-20));

    [Fact]
    public async Task Doctor_finishes_his_visit()
    {
        var id = await InProgress();
        var status = await NewSvc().FinishAppointmentTime(C.Id, Doc.UserId, id);

        status.ShouldBe("Completed");
        var a = await LoadAsync(id);
        a.appointmentStatus.ShouldBe(AppointmentStatus.Completed);
        ShouldBeNowUtc(a.actualEndTime!.Value); 
        a.actualEndTime!.Value.ShouldBeGreaterThan(a.actualStartTime!.Value);
        (await CountAuditAsync("FinishAppointment", id)).ShouldBe(1);
    }

    [Fact]
    public async Task Finishing_twice_is_refused()
    {
        var id = await InProgress();
        await NewSvc().FinishAppointmentTime(C.Id, Doc.UserId, id);
        var end = (await LoadAsync(id)).actualEndTime;

        await ShouldFailWith("Appointment.NotFound", () => NewSvc().FinishAppointmentTime(C.Id, Doc.UserId, id));
        (await LoadAsync(id)).actualEndTime.ShouldBe(end);
    }

    [Fact]
    public async Task A_doctor_cannot_finish_another_doctors_visit()
    {
        var other = await C.AddDoctorAsync();
        var id = await InProgress(other);
        await ShouldFailWith("Appointment.NotFound", () => NewSvc().FinishAppointmentTime(C.Id, Doc.UserId, id));
        (await LoadAsync(id)).appointmentStatus.ShouldBe(AppointmentStatus.InProgress);
    }

    [Fact]
    public async Task A_visit_that_was_not_started_cannot_be_finished()
    {
        var id = await AddTodayAsync(checkedIn: true);                 // still Confirmed
        await ShouldFailWith("Appointment.NotFound", () => NewSvc().FinishAppointmentTime(C.Id, Doc.UserId, id));
    }

    [Fact]
    public async Task InProgress_without_a_start_time_is_NotStartedYet()
    {
        var id = await AddTodayAsync(AppointmentStatus.InProgress, checkedIn: true);
        await ShouldFailWith("Appointment.NotStartedYet", () => NewSvc().FinishAppointmentTime(C.Id, Doc.UserId, id));
    }

    [Fact]
    public async Task InProgress_with_an_end_time_is_AlreadyFinished()
    {
        var id = await AddTodayAsync(AppointmentStatus.InProgress, checkedIn: true,
                                     start: DateTime.UtcNow.AddMinutes(-30), end: DateTime.UtcNow.AddMinutes(-5));
        await ShouldFailWith("Appointment.AlreadyFinished", () => NewSvc().FinishAppointmentTime(C.Id, Doc.UserId, id));
    }

    [Fact]
    public async Task Inactive_doctor_a_non_doctor_and_a_suspended_center_are_refused()
    {
        var id = await InProgress();

        var off = await C.AddDoctorAsync(active: false);
        await ShouldFailWith("Doctor.NotFound", () => NewSvc().FinishAppointmentTime(C.Id, off.UserId, id));
        await ShouldFailWith("Doctor.NotFound", () => NewSvc().FinishAppointmentTime(C.Id, Reception, id));

        await SuspendCenterAsync();
        await ShouldFailWith("Center.NotFound", () => NewSvc().FinishAppointmentTime(C.Id, Doc.UserId, id));
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Two_simultaneous_finishes_only_one_wins()
    {
        var id = await InProgress();
        var gate = new TaskCompletionSource();
        async Task<bool> Try()
        {
            await gate.Task;
            try { await NewSvc().FinishAppointmentTime(C.Id, Doc.UserId, id); return true; }
            catch (BusinessException) { return false; }
        }
        var tasks = Enumerable.Range(0, 4).Select(_ => Task.Run(Try)).ToArray();
        gate.SetResult();
        (await Task.WhenAll(tasks)).Count(ok => ok).ShouldBe(1);
        (await CountAuditAsync("FinishAppointment", id)).ShouldBe(1);
    }

    [Fact]
    public async Task Whole_visit_flow_checkin_call_finish()
    {
        var id = await AddTodayAsync();
        await NewSvc().ReceptionistCheckInAppointmentAsync(C.Id, Reception, id);
        await NewSvc().NextPatient(C.Id, Reception, id);
        await NewSvc().FinishAppointmentTime(C.Id, Doc.UserId, id);

        var a = await LoadAsync(id);
        a.appointmentStatus.ShouldBe(AppointmentStatus.Completed);
        a.CheckInTime.ShouldNotBeNull();
        a.actualStartTime!.Value.ShouldBeGreaterThanOrEqualTo(a.CheckInTime!.Value);
        a.actualEndTime!.Value.ShouldBeGreaterThanOrEqualTo(a.actualStartTime!.Value);
    }
}
