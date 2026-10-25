using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.AppointmentDto;
using Sehatak.Domain.Enums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Appointments;

/// <summary>AppointmentService.GetAvailableDoctorSlot (what the patient sees before booking).</summary>
[Collection(SqlServerCollection.Name)]
public class GetAvailableSlotsTests(SqlServerFixture fx) : AppointmentTestBase(fx)
{
    private Task<AvailableDoctorSlot> Get(DateOnly? date = null, int? doctorId = null, int? centerId = null) =>
        C.NewAppointmentService().GetAvailableDoctorSlot(centerId ?? C.Id, doctorId ?? Doc.DoctorId, date ?? Date);

    [Fact]
    public async Task Free_day_returns_every_slot_in_order_with_doctor_details()
    {
        var res = await Get();

        res.dateAvailable.ShouldBe(new[] { S0900, S0930, S1000, S1030 });
        res.doctorId.ShouldBe(Doc.DoctorId);
        res.date.ShouldBe(Date);
        res.DayOfWeek.ShouldBe(DayOfWeek.Monday);
        res.doctorName.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Confirmed_appointment_removes_its_slot()
    {
        await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0930);

        (await Get()).dateAvailable.ShouldBe(new[] { S0900, S1000, S1030 });
    }

    [Fact]
    public async Task Cancelled_appointment_does_not_hold_its_slot()
    {
        await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0930, AppointmentStatus.Cancelled);

        (await Get()).dateAvailable.ShouldContain(S0930);
    }

    [Fact]
    public async Task Blocked_slot_is_removed()
    {
        await C.BlockAsync(Doc.DoctorId, Date, S1000);

        (await Get()).dateAvailable.ShouldBe(new[] { S0900, S0930, S1030 });
    }

    [Fact]
    public async Task Whole_day_block_returns_an_empty_list_not_an_error()
    {
        await C.BlockAsync(Doc.DoctorId, Date, slot: null);

        (await Get()).dateAvailable.ShouldBeEmpty();
    }

    [Fact]
    public async Task Blocking_one_day_does_not_affect_the_next_week()
    {
        await C.BlockAsync(Doc.DoctorId, Date, slot: null);

        (await Get(Date.AddDays(7))).dateAvailable.Count.ShouldBe(4);
    }

    // ------------------------------------------------------------------ business errors

    [Fact]
    public Task Date_Invalid_for_the_past() =>
        ShouldFailWith("Date.Invalid", () => Get(ClinicClock.Today.AddDays(-2)));

    [Fact]
    public Task Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => Get(centerId: 999_999));

    [Fact]
    public Task Doctor_NotFound_for_an_unknown_doctor() =>
        ShouldFailWith("Doctor.NotFound", () => Get(doctorId: 999_999));

    [Fact]
    public async Task Doctor_NotFound_for_an_inactive_doctor()
    {
        var inactive = await C.AddDoctorAsync(active: false);
        await ShouldFailWith("Doctor.NotFound", () => Get(doctorId: inactive.DoctorId));
    }

    [Fact]
    public Task Schedule_NotFound_on_a_non_working_day() =>
        ShouldFailWith("Schedule.NotFound", () => Get(TestCenter.FutureDate(DayOfWeek.Tuesday)));

    [Fact]
    public async Task Schedule_SlotDurationMissing_when_duration_is_not_set()
    {
        var doc2 = await C.AddDoctorAsync();
        await C.AddScheduleAsync(doc2.DoctorId, DayOfWeek.Monday, S0900, new TimeOnly(11, 0), slotMinutes: null);

        await ShouldFailWith("Schedule.SlotDurationMissing", () => Get(doctorId: doc2.DoctorId));
    }

    // ------------------------------------------------------------------ KNOWN BUGS (expected to FAIL until fixed)

    /// <summary>
    /// The booked-slot filter only counts Confirmed appointments, but the unique index also covers
    /// Pending / CheckedIn / InProgress / Completed. After check-in the slot is shown as free again.
    /// Fix: treat those statuses as "taking" the slot (same list as the index filter) in every slot query.
    /// </summary>
    [Theory]
    [Trait("Category", "KnownBug")]
    [InlineData(AppointmentStatus.CheckedIn)]
    [InlineData(AppointmentStatus.InProgress)]
    [InlineData(AppointmentStatus.Completed)]
    public async Task Slot_held_by_an_active_or_finished_appointment_is_not_offered(AppointmentStatus status)
    {
        await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900, status);

        (await Get()).dateAvailable.ShouldNotContain(S0900, $"a {status} appointment still occupies 09:00");
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Booking_a_slot_held_by_a_CheckedIn_appointment_is_refused_gracefully()
    {
        var other = await C.AddPatientAsync();
        await C.AddAppointmentAsync(Doc.DoctorId, other.PatientId, Date, S0900, AppointmentStatus.CheckedIn);

        BookAppointmentRespesponse? res = null;
        await Should.NotThrowAsync(async () => res = await C.NewAppointmentService().BookAppointmentAsync(
            C.Id, Doc.DoctorId, Pat.UserId, new BookAppointmentRequest { timeSlot = S0900, dateOnly = Date }));

        // today it passes every check and dies on the unique index (DbUpdateException => HTTP 500)
        res!.Success.ShouldBeFalse();
        await using var db = Db();
        (await db.Appointments.CountAsync(a => a.timeSlot == S0900 && a.appointmentDate == Date)).ShouldBe(1);
    }

    /// <summary>Same UTC-vs-clinic-time problem as booking: slots that already passed (clinic time) are still offered.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Slots_that_already_passed_today_in_clinic_time_are_not_offered()
    {
        var local = ClinicClock.Now;
        if (local.Hour < 4) return;

        var past = new TimeOnly(local.Hour - 1, 0);
        var doc2 = await C.AddDoctorAsync();
        await C.AddScheduleAsync(doc2.DoctorId, ClinicClock.Today.DayOfWeek, past, past.AddHours(1), 30);

        var res = await Get(ClinicClock.Today, doc2.DoctorId);

        res.dateAvailable.ShouldNotContain(past, $"{past} already passed (clinic time now {local:HH:mm})");
    }
}
