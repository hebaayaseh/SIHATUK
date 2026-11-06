using Shouldly;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Domain.Enums;
using Sehatak.IntegrationTests.Infrastructure;

namespace Sehatak.IntegrationTests.CheckTime;

[Collection(SqlServerCollection.Name)]
public class ReceptionistCheckInTests(SqlServerFixture fx) : CheckTimeTestBase(fx)
{
    [Fact]
    public async Task Check_in_stores_now_in_utc_keeps_the_status_and_writes_audit()
    {
        var id = await AddTodayAsync();
        var msg = await NewSvc().ReceptionistCheckInAppointmentAsync(C.Id, Reception, id);

        msg.ShouldNotBeNullOrWhiteSpace();
        var a = await LoadAsync(id);
        ShouldBeNowUtc(a.CheckInTime!.Value);
        a.appointmentStatus.ShouldBe(AppointmentStatus.Confirmed);
        (await CountAuditAsync("ReceptionistCheckInAppointment", id)).ShouldBe(1);
    }

    [Fact]
    public async Task A_second_check_in_is_refused_and_keeps_the_first_time()
    {
        var id = await AddTodayAsync();
        await NewSvc().ReceptionistCheckInAppointmentAsync(C.Id, Reception, id);
        var first = (await LoadAsync(id)).CheckInTime;

        await ShouldFailWith("Appointment.AlreadyCheckedIn", () => NewSvc().ReceptionistCheckInAppointmentAsync(C.Id, Reception, id));
        (await LoadAsync(id)).CheckInTime.ShouldBe(first);
    }

    [Theory]
    [InlineData(AppointmentStatus.Pending)]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.NoShow)]
    [InlineData(AppointmentStatus.InProgress)]
    public async Task Only_confirmed_appointments_can_be_checked_in(AppointmentStatus status)
    {
        var id = await AddTodayAsync(status);
        await ShouldFailWith("Appointment.NotFound", () => NewSvc().ReceptionistCheckInAppointmentAsync(C.Id, Reception, id));
    }

    [Fact]
    public async Task Unknown_appointment_is_NotFound()
        => await ShouldFailWith("Appointment.NotFound", () => NewSvc().ReceptionistCheckInAppointmentAsync(C.Id, Reception, 999999));

    [Fact]
    public async Task Inactive_receptionist_is_refused()
    {
        var off = await C.AddReceptionistAsync(active: false);
        var id = await AddTodayAsync();
        await ShouldFailWith("User.NotFound", () => NewSvc().ReceptionistCheckInAppointmentAsync(C.Id, off, id));
    }

    [Fact]
    public async Task Suspended_center_is_refused()
    {
        var id = await AddTodayAsync();
        await SuspendCenterAsync();
        await ShouldFailWith("Center.NotFound", () => NewSvc().ReceptionistCheckInAppointmentAsync(C.Id, Reception, id));
    }

    // ------------------------------------------------------------------ known bugs

    /// <summary>Tomorrow's patient can be "checked in" today: wrong queue and wrong wait-time stats.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Check_in_is_only_allowed_on_the_day_of_the_appointment()
    {
        var future = await AddTodayAsync(date: Date);                 // a future Monday
        await ShouldFailWith("Appointment.NotToday", () => NewSvc().ReceptionistCheckInAppointmentAsync(C.Id, Reception, future));

        var past = await AddTodayAsync(date: Today.AddDays(-3));
        await ShouldFailWith("Appointment.NotToday", () => NewSvc().ReceptionistCheckInAppointmentAsync(C.Id, Reception, past));
    }

    /// <summary>Two receptionists (or a double click) both pass the "CheckInTime == null" check and both succeed.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Two_simultaneous_check_ins_only_one_wins()
    {
        var id = await AddTodayAsync();
        var gate = new TaskCompletionSource();
        async Task<bool> Try()
        {
            await gate.Task;
            try { await NewSvc().ReceptionistCheckInAppointmentAsync(C.Id, Reception, id); return true; }
            catch (BusinessException) { return false; }
        }
        var tasks = Enumerable.Range(0, 4).Select(_ => Task.Run(Try)).ToArray();
        gate.SetResult();
        (await Task.WhenAll(tasks)).Count(ok => ok).ShouldBe(1);
        (await CountAuditAsync("ReceptionistCheckInAppointment", id)).ShouldBe(1);
    }
}
