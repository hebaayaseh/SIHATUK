using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.AppointmentDto;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Appointments;

/// <summary>
/// When an appointment is cancelled (by the patient or by the receptionist) the first waiting patient of that doctor and day
/// takes the freed slot. The waiting-list row is claimed atomically, so a second cancel, a receptionist or a job can never give
/// the same patient two appointments.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class CancelPromotionTests(SqlServerFixture fx) : AppointmentTestBase(fx)
{
    private static readonly TimeOnly T09 = new(9, 0), T10 = new(10, 0), T11 = new(11, 0), T12 = new(12, 0);
    private static DateOnly Future => ClinicClock.Today.AddDays(3);

    private static async Task ShouldFailWith(string code, Func<Task> act)
    {
        var ex = await Should.ThrowAsync<BusinessException>(act);
        ex.Message.ShouldBe(code);
    }

    private async Task<int> NewDoctorAsync(bool withSchedule = false)
    {
        var d = await C.AddDoctorAsync();
        if (withSchedule) await C.AddScheduleAsync(d.DoctorId, Future.DayOfWeek, T09, T12, 60);
        return d.DoctorId;
    }

    private async Task<int> AddWaitlistAsync(int doctorId, int patientId, DateOnly date, WaitlistStatus status = WaitlistStatus.Waiting, DateTime? createdAt = null)
    {
        await using var db = Db();
        var w = new Waitlist { DoctorId = doctorId, PatientId = patientId, PreferredDate = date, Status = status, CreatedAt = createdAt ?? DateTime.UtcNow };
        db.Waitlists.Add(w);
        await db.SaveChangesAsync();
        return w.Id;
    }

    private async Task<Waitlist> WaitAsync(int id)
    {
        await using var db = Db();
        return await db.Waitlists.AsNoTracking().SingleAsync(w => w.Id == id);
    }

    private async Task<List<Appointment>> ActiveAsync(int doctorId, DateOnly date)
    {
        await using var db = Db();
        return await db.Appointments.AsNoTracking()
            .Where(a => a.doctorId == doctorId && a.appointmentDate == date && a.appointmentStatus != AppointmentStatus.Cancelled)
            .ToListAsync();
    }

    private Task<string> PatientCancelsAsync(int doctorId, int patientUserId, TimeOnly slot) =>
        C.NewAppointmentService().CancelAppointmentAsync(C.Id, doctorId, patientUserId,
            new CancelAppointmentRequest { timeSlot = slot, date = Future, Resone = "x" });

    /// <summary>A doctor, a patient who holds <paramref name="slot"/>, and the waiting patients (oldest first).</summary>
    private async Task<(int Doctor, int HolderUser, int HolderPatient, int[] Waiting, int[] WaitingPatients)> SetupAsync(TimeOnly slot, int waiting)
    {
        var doc = await NewDoctorAsync();
        var holder = await C.AddPatientAsync();
        await C.AddAppointmentAsync(doc, holder.PatientId, Future, slot);
        var ids = new List<int>();
        var patients = new List<int>();
        for (var i = 0; i < waiting; i++)
        {
            var p = await C.AddPatientAsync();
            patients.Add(p.PatientId);
            ids.Add(await AddWaitlistAsync(doc, p.PatientId, Future, createdAt: DateTime.UtcNow.AddMinutes(-60 + i)));
        }
        return (doc, holder.UserId, holder.PatientId, ids.ToArray(), patients.ToArray());
    }

    [Fact]
    public async Task When_a_patient_cancels_the_first_waiting_patient_takes_the_freed_slot()
    {
        var s = await SetupAsync(T09, waiting: 2);

        await PatientCancelsAsync(s.Doctor, s.HolderUser, T09);

        var active = await ActiveAsync(s.Doctor, Future);
        var promoted = active.ShouldHaveSingleItem();
        promoted.patientId.ShouldBe(s.WaitingPatients[0]);
        promoted.timeSlot.ShouldBe(T09);
        promoted.appointmentStatus.ShouldBe(AppointmentStatus.Confirmed);

        var first = await WaitAsync(s.Waiting[0]);
        first.Status.ShouldBe(WaitlistStatus.Entered);
        first.PromotedAppointmentId.ShouldBe(promoted.Id, "the waiting-list row must point to the appointment it became");
        (await WaitAsync(s.Waiting[1])).Status.ShouldBe(WaitlistStatus.Waiting);

        await using var db = Db();
        (await db.Appointments.AsNoTracking().SingleAsync(a => a.patientId == s.HolderPatient)).appointmentStatus.ShouldBe(AppointmentStatus.Cancelled);
        var promotedUser = (await db.Patients.AsNoTracking().SingleAsync(p => p.patientId == s.WaitingPatients[0])).NotifiableUserId;
        (await db.Notifications.CountAsync(n => n.UserId == promotedUser && n.Type == NotificationType.Appointment)).ShouldBe(1);
    }

    [Fact]
    public async Task When_the_receptionist_cancels_the_waiting_patient_takes_the_slot_under_that_receptionist()
    {
        var s = await SetupAsync(T10, waiting: 1);
        var rec = await C.AddReceptionistAsync();

        await C.NewAppointmentService().ReceptionistCancelAppointmentAsync(C.Id, s.Doctor, rec,
            new ReceptionistCancelAppointmentRequest { PatientId = s.HolderPatient, timeSlot = T10, date = Future, Resone = "x" });

        var promoted = (await ActiveAsync(s.Doctor, Future)).ShouldHaveSingleItem();
        promoted.patientId.ShouldBe(s.WaitingPatients[0]);
        promoted.timeSlot.ShouldBe(T10);
        promoted.ReceptionistId.ShouldBe(rec);
        (await WaitAsync(s.Waiting[0])).PromotedAppointmentId.ShouldBe(promoted.Id);
    }

    [Fact]
    public async Task A_waiting_patient_who_already_has_an_appointment_that_day_is_skipped_and_stays_on_the_list()
    {
        var s = await SetupAsync(T09, waiting: 2);
        await C.AddAppointmentAsync(s.Doctor, s.WaitingPatients[0], Future, T11);      // the first in line already holds 11:00

        await PatientCancelsAsync(s.Doctor, s.HolderUser, T09);

        (await WaitAsync(s.Waiting[0])).Status.ShouldBe(WaitlistStatus.Waiting);
        (await WaitAsync(s.Waiting[1])).Status.ShouldBe(WaitlistStatus.Entered);
        var active = await ActiveAsync(s.Doctor, Future);
        active.Count.ShouldBe(2);
        active.Count(a => a.patientId == s.WaitingPatients[0]).ShouldBe(1, "he must not end up with two appointments");
        active.Single(a => a.patientId == s.WaitingPatients[1]).timeSlot.ShouldBe(T09);
    }

    [Fact]
    public async Task Without_anyone_waiting_the_cancel_just_cancels()
    {
        var s = await SetupAsync(T09, waiting: 0);

        var msg = await PatientCancelsAsync(s.Doctor, s.HolderUser, T09);

        msg.ShouldNotBeNullOrWhiteSpace();
        (await ActiveAsync(s.Doctor, Future)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Waiting_entries_of_another_day_another_doctor_or_not_waiting_are_not_promoted()
    {
        var s = await SetupAsync(T09, waiting: 0);
        var otherDoctor = await NewDoctorAsync();
        var p = async () => (await C.AddPatientAsync()).PatientId;
        var otherDay = await AddWaitlistAsync(s.Doctor, await p(), Future.AddDays(1));
        var otherDoc = await AddWaitlistAsync(otherDoctor, await p(), Future);
        var cancelled = await AddWaitlistAsync(s.Doctor, await p(), Future, WaitlistStatus.Cancelled);
        var entered = await AddWaitlistAsync(s.Doctor, await p(), Future, WaitlistStatus.Entered);

        await PatientCancelsAsync(s.Doctor, s.HolderUser, T09);

        (await ActiveAsync(s.Doctor, Future)).ShouldBeEmpty();
        (await WaitAsync(otherDay)).Status.ShouldBe(WaitlistStatus.Waiting);
        (await WaitAsync(otherDoc)).Status.ShouldBe(WaitlistStatus.Waiting);
        (await WaitAsync(cancelled)).Status.ShouldBe(WaitlistStatus.Cancelled);
        (await WaitAsync(entered)).Status.ShouldBe(WaitlistStatus.Entered);
    }

    [Fact]
    public async Task Cancelling_the_same_appointment_twice_at_once_gives_the_slot_to_one_waiting_patient_only()
    {
        var s = await SetupAsync(T09, waiting: 2);

        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            try { await PatientCancelsAsync(s.Doctor, s.HolderUser, T09); return "ok"; }
            catch (BusinessException e) { return e.Message; }      // a database exception would escape and fail the test
        })));

        results.Count(r => r == "ok").ShouldBeGreaterThanOrEqualTo(1);
        var active = await ActiveAsync(s.Doctor, Future);
        active.ShouldHaveSingleItem().timeSlot.ShouldBe(T09);

        var rows = new[] { await WaitAsync(s.Waiting[0]), await WaitAsync(s.Waiting[1]) };
        rows.Count(r => r.Status == WaitlistStatus.Entered).ShouldBe(1, "exactly one waiting patient got the slot");
        rows.Count(r => r.Status == WaitlistStatus.Waiting).ShouldBe(1, "the other one is back on the list, not lost");
    }

    [Fact]
    public async Task A_cancel_and_the_receptionist_bringing_in_the_same_patient_at_once_never_give_him_two_appointments()
    {
        var doc = await NewDoctorAsync(withSchedule: true);
        var holder = await C.AddPatientAsync();
        await C.AddAppointmentAsync(doc, holder.PatientId, Future, T09);
        await C.AddAppointmentAsync(doc, (await C.AddPatientAsync()).PatientId, Future, T10);
        await C.AddAppointmentAsync(doc, (await C.AddPatientAsync()).PatientId, Future, T11);
        var waiting = await C.AddPatientAsync();
        var wid = await AddWaitlistAsync(doc, waiting.PatientId, Future);
        var rec = await C.AddReceptionistAsync();

        await Task.WhenAll(
            Task.Run(async () =>
            {
                try { await PatientCancelsAsync(doc, holder.UserId, T09); } catch (BusinessException) { }
            }),
            Task.Run(async () =>
            {
                try
                {
                    await C.NewAppointmentService().ReceptionistEnterFromWaitlistAsync(C.Id, doc, rec,
                        new ReceptionistEnterFromWaitlistRequest { WaitListId = wid });
                }
                catch (BusinessException) { }
            }));

        var mine = (await ActiveAsync(doc, Future)).Where(a => a.patientId == waiting.PatientId).ToList();
        mine.Count.ShouldBe(1, "whoever got there first wins; the other one must find the patient already taken");
        (await WaitAsync(wid)).Status.ShouldBe(WaitlistStatus.Entered);
    }
}
