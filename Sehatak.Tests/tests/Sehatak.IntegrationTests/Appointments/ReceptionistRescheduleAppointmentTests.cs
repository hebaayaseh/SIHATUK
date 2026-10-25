using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.AppointmentDto;
using Sehatak.Domain.Enums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Appointments;

/// <summary>AppointmentService.ReceptionistRescheduleAppointmentAsync.</summary>
[Collection(SqlServerCollection.Name)]
public class ReceptionistRescheduleAppointmentTests(SqlServerFixture fx) : AppointmentTestBase(fx)
{
    private int _rec;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _rec = await C.AddReceptionistAsync();
    }

    private ReceptionistRescheduleAppointmentRequest Move(int appointmentId, TimeOnly slot, DateOnly? date = null, int? patientId = null, int? sub = null) =>
        new() { appointmentId = appointmentId, PatientId = patientId ?? Pat.PatientId, timeSlot = slot, date = date ?? Date, SubPatientId = sub };

    private Task<BookAppointmentRespesponse> DoMove(ReceptionistRescheduleAppointmentRequest req, int? userId = null, int? doctorId = null, int? centerId = null) =>
        C.NewAppointmentService().ReceptionistRescheduleAppointmentAsync(centerId ?? C.Id, doctorId ?? Doc.DoctorId, userId ?? _rec, req);

    [Fact]
    public async Task Moves_to_a_free_slot_and_counts_the_reschedule()
    {
        var id = await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900);

        (await DoMove(Move(id, S1000))).Success.ShouldBeTrue();

        await using var db = Db();
        var appt = await db.Appointments.SingleAsync(a => a.Id == id);
        appt.timeSlot.ShouldBe(S1000);
        appt.RescheduleCount.ShouldBe(1);
        (await db.Notifications.CountAsync(n => n.UserId == Pat.UserId && n.Type == NotificationType.Appointment)).ShouldBe(1);
        (await db.AuditLogs.CountAsync(a => a.Action == "RescheduleAppointment" && a.EntityId == id)).ShouldBe(1);
    }

    [Fact]
    public async Task Moves_to_another_day()
    {
        var id = await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900);

        (await DoMove(Move(id, S0930, Date.AddDays(7)))).Success.ShouldBeTrue();

        await using var db = Db();
        (await db.Appointments.SingleAsync(a => a.Id == id)).appointmentDate.ShouldBe(Date.AddDays(7));
    }

    [Fact]
    public async Task Moves_a_sub_patient_appointment()
    {
        var sub = await C.AddSubPatientAsync(Pat);
        var id = await C.AddAppointmentAsync(Doc.DoctorId, sub.PatientId, Date, S0900);

        (await DoMove(Move(id, S1030, sub: sub.PatientId))).Success.ShouldBeTrue();
    }

    [Fact]
    public async Task Taken_slot_is_refused_with_alternatives()
    {
        var other = await C.AddPatientAsync();
        var id = await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900);
        await C.AddAppointmentAsync(Doc.DoctorId, other.PatientId, Date, S1000);

        var res = await DoMove(Move(id, S1000));

        res.Success.ShouldBeFalse();
        res.AlternativeSlots!.ShouldNotContain(S1000);
        await using var db = Db();
        (await db.Appointments.SingleAsync(a => a.Id == id)).timeSlot.ShouldBe(S0900);
    }

    [Fact]
    public async Task After_three_reschedules_the_fourth_is_refused()
    {
        var id = await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900);
        await using (var db = Db())
        {
            (await db.Appointments.SingleAsync(a => a.Id == id)).RescheduleCount = 3;
            await db.SaveChangesAsync();
        }

        (await DoMove(Move(id, S1000))).Success.ShouldBeFalse();

        await using var check = Db();
        (await check.Appointments.SingleAsync(a => a.Id == id)).timeSlot.ShouldBe(S0900);
    }

    // ------------------------------------------------------------------ business errors

    [Fact]
    public async Task Date_Invalid_for_the_past()
    {
        var id = await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900);
        await ShouldFailWith("Date.Invalid", () => DoMove(Move(id, S1000, ClinicClock.Today.AddDays(-2))));
    }

    [Fact]
    public Task Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => DoMove(Move(1, S1000), centerId: 999_999));

    [Fact]
    public Task Receptionist_NotFound_when_the_user_is_not_a_receptionist() =>
        ShouldFailWith("Receptionist.NotFound", () => DoMove(Move(1, S1000), userId: C.AdminUserId));

    [Fact]
    public Task Patient_NotFound_for_an_unknown_patient() =>
        ShouldFailWith("Patient.NotFound", () => DoMove(Move(1, S1000, patientId: 999_999)));

    [Fact]
    public async Task SubPatient_NotFoundOrNotOwned_for_someone_elses_child()
    {
        var stranger = await C.AddPatientAsync();
        var child = await C.AddSubPatientAsync(stranger);
        var id = await C.AddAppointmentAsync(Doc.DoctorId, child.PatientId, Date, S0900);

        await ShouldFailWith("SubPatient.NotFoundOrNotOwned", () => DoMove(Move(id, S1000, sub: child.PatientId)));
    }

    [Fact]
    public async Task Doctor_NotFound()
    {
        var id = await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900);
        await ShouldFailWith("Doctor.NotFound", () => DoMove(Move(id, S1000), doctorId: 999_999));
    }

    [Fact]
    public Task Appointment_NotFound_for_an_unknown_id() =>
        ShouldFailWith("Appointment.NotFound", () => DoMove(Move(999_999, S1000)));

    [Fact]
    public async Task Appointment_NotFound_when_it_belongs_to_another_patient()
    {
        var other = await C.AddPatientAsync();
        var id = await C.AddAppointmentAsync(Doc.DoctorId, other.PatientId, Date, S0900);

        await ShouldFailWith("Appointment.NotFound", () => DoMove(Move(id, S1000)));
    }

    [Fact]
    public async Task Appointment_NotFound_when_it_is_not_Confirmed()
    {
        var id = await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900, AppointmentStatus.Cancelled);
        await ShouldFailWith("Appointment.NotFound", () => DoMove(Move(id, S1000)));
    }

    [Fact]
    public async Task Doctor_DayBlocked_when_the_target_day_is_blocked()
    {
        var id = await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900);
        await C.BlockAsync(Doc.DoctorId, Date.AddDays(7), slot: null);

        await ShouldFailWith("Doctor.DayBlocked", () => DoMove(Move(id, S1000, Date.AddDays(7))));
    }

    [Fact]
    public async Task Schedule_NotFound_when_the_doctor_does_not_work_that_day()
    {
        var id = await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900);
        await ShouldFailWith("Schedule.NotFound",
            () => DoMove(Move(id, S1000, TestCenter.FutureDate(DayOfWeek.Tuesday))));
    }

    [Fact]
    public async Task Schedule_SlotDurationMissing_when_the_target_day_has_no_duration()
    {
        var id = await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900);
        await C.AddScheduleAsync(Doc.DoctorId, DayOfWeek.Wednesday, S0900, new TimeOnly(11, 0), slotMinutes: null);

        await ShouldFailWith("Schedule.SlotDurationMissing",
            () => DoMove(Move(id, S1000, TestCenter.FutureDate(DayOfWeek.Wednesday))));
    }
}
