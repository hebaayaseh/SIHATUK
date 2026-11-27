using System.Net;
using Microsoft.EntityFrameworkCore;
using Sehatak.Domain.Enums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Api;

/// <summary>E2E batch 1: the patient's journey over real HTTP, from the landing page to the dashboard.</summary>
[Collection(SqlServerCollection.Name)]
public class PatientJourneyFlowTests(SqlServerFixture fx) : FlowTestBase(fx)
{
    private async Task FillTheDayAsync()
    {
        foreach (var slot in new[] { S0900, S0930, S1000, S1030 })
        {
            var p = await C.AddPatientAsync();
            await C.AddAppointmentAsync(Doc.DoctorId, p.PatientId, Date, slot);
        }
    }

    // ---------- landing page / browsing (anonymous) ----------

    [Fact]
    public async Task Landing_search_finds_the_center_by_name_and_hides_private_fields()
    {
        var marker = "Zeta" + Guid.NewGuid().ToString("N")[..8];
        await using (var shared = Fx.Harness.NewSharedDb())
        {
            var center = await shared.MedicalCenters.SingleAsync(c => c.Id == C.Id);
            center.Name = $"{marker} Clinic";
            await shared.SaveChangesAsync();
        }

        var res = await GetAsync($"/Center/patient-search-centers?Search={marker}&PageNumber=1&PageSize=10");
        var json = await OkJsonAsync(res);

        json.GetProperty("items").GetArrayLength().ShouldBe(1);
        JsonContains(json, marker).ShouldBeTrue();
        var raw = json.ToString();
        raw.ShouldNotContain("adminEmail", Case.Insensitive);
        raw.ShouldNotContain("prepayment", Case.Insensitive);
    }

    [Fact]
    public async Task Anonymous_visitor_can_list_doctors_and_see_free_slots()
    {
        var doctors = await GetAsync($"/Appointment/patient-get-doctors/{C.Id}?PageNumber=1&PageSize=10");
        (await OkJsonAsync(doctors)).GetProperty("items").GetArrayLength().ShouldBeGreaterThanOrEqualTo(1);

        (await GetAsync($"/Appointment/patient-get-doctor/{C.Id}/{Doc.DoctorId}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await FreeSlotsAsync()).ShouldBe(new[] { "09:00", "09:30", "10:00", "10:30" });
    }

    [Fact]
    public async Task A_day_the_doctor_does_not_work_has_no_free_slots()
    {
        var tuesday = Date.AddDays(1).ToString("yyyy-MM-dd");
        var res = await GetAsync($"/Appointment/patient-get-available-doctor-slot/{C.Id}/{Doc.DoctorId}?date={tuesday}");

        ((int)res.StatusCode).ShouldBeLessThan(500);
        if (res.StatusCode == HttpStatusCode.OK)
            (await JsonAsync(res)).GetProperty("dateAvailable").GetArrayLength().ShouldBe(0);
    }

    // ---------- booking ----------

    [Fact]
    public async Task Register_book_then_the_slot_disappears_and_the_dashboard_shows_it()
    {
        var p = await RegisterVerifyLoginAsync();
        var appt = await BookAsync(p, "09:00:00");

        (await FreeSlotsAsync()).ShouldBe(new[] { "09:30", "10:00", "10:30" });

        var dash = await GetAsync(
            $"/AppointmentDashboard/patient-appointment-dashboard/{C.Id}?date={DateStr}&PageNumber=1&PageSize=10&status=Confirmed", p.Token);
        var json = await OkJsonAsync(dash);
        json.GetProperty("items").GetArrayLength().ShouldBe(1);
        json.GetProperty("items")[0].GetProperty("appointmentId").GetInt32().ShouldBe(appt);
        json.GetProperty("items")[0].GetProperty("timeSlot").GetString().ShouldStartWith("09:00");
    }

    [Fact]
    public async Task A_second_patient_booking_the_same_slot_gets_a_polite_answer_with_alternatives()
    {
        var a = await RegisterVerifyLoginAsync("Aya");
        var b = await RegisterVerifyLoginAsync("Bana");
        await BookAsync(a, "09:00:00");

        var res = await BookRaw(b.Token, "09:00:00");

        var json = await OkJsonAsync(res);
        json.GetProperty("success").GetBoolean().ShouldBeFalse();
        json.GetProperty("alternativeSlots").GetArrayLength().ShouldBeGreaterThan(0);

        await using var db = Db();
        (await db.Appointments.CountAsync(x => x.appointmentDate == Date && x.timeSlot == S0900)).ShouldBe(1);
    }

    [Fact]
    public async Task Booking_needs_a_patient_token_for_this_center()
    {
        var p = await RegisterVerifyLoginAsync();
        var other = await Fx.Harness.CreateCenterAsync();
        try
        {
            (await BookRaw("", "09:00:00")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

            var anon = await PostAsync($"/Appointment/patient-book/{C.Id}/{Doc.DoctorId}", new { timeSlot = "09:00:00", dateOnly = DateStr });
            anon.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

            (await BookRaw(DoctorToken, "09:00:00")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await BookRaw(Token("Receptionist", C.Id, 1), "09:00:00")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

            var foreign = Token("Patient", other.Id, p.UserId);
            ((int)(await BookRaw(foreign, "09:00:00")).StatusCode).ShouldBeInRange(401, 403);
        }
        finally { await Fx.Harness.DropTenantAsync(other.Id); }

        await using var db = Db();
        (await db.Appointments.CountAsync(a => a.patientId == p.PatientId)).ShouldBe(0);
    }

    [Fact]
    public async Task Bad_input_is_answered_with_400_never_500()
    {
        var p = await RegisterVerifyLoginAsync();
        var yesterday = Date.AddDays(-30).ToString("yyyy-MM-dd");

        (await BookRaw(p.Token, "09:00:00", date: yesterday)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await BookRaw(p.Token, "09:00:00", date: Date.AddDays(1).ToString("yyyy-MM-dd")))
            .StatusCode.ShouldBeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.OK);
        (await BookRaw(p.Token, "09:00:00", doctorId: 99999)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var garbage = await SendAsync(HttpMethod.Post, $"/Appointment/patient-book/{C.Id}/{Doc.DoctorId}", p.Token,
            new { timeSlot = "not-a-time", dateOnly = "x" });
        garbage.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // 03:00 is outside the doctor's hours
        var res = await BookRaw(p.Token, "03:00:00");
        ((int)res.StatusCode).ShouldBeLessThan(500);
        if (res.StatusCode == HttpStatusCode.OK)
            (await JsonAsync(res)).GetProperty("success").GetBoolean().ShouldBeFalse();

        await using var db = Db();
        (await db.Appointments.CountAsync(a => a.patientId == p.PatientId)).ShouldBe(0);
    }

    // ---------- cancel / reschedule ----------

    [Fact]
    public async Task Cancel_frees_the_slot_again()
    {
        var p = await RegisterVerifyLoginAsync();
        await BookAsync(p, "09:00:00");

        var cancel = await PostAsync($"/Appointment/patient-cancel-appointment/{C.Id}/{Doc.DoctorId}",
            new { timeSlot = "09:00:00", date = DateStr, resone = "changed my mind" }, p.Token);
        cancel.StatusCode.ShouldBe(HttpStatusCode.OK, await cancel.Content.ReadAsStringAsync());

        (await FreeSlotsAsync()).ShouldContain("09:00");
        await using var db = Db();
        (await db.Appointments.SingleAsync(a => a.patientId == p.PatientId)).appointmentStatus.ShouldBe(AppointmentStatus.Cancelled);
    }

    [Fact]
    public async Task Cancelling_gives_the_slot_to_the_patient_waiting_on_the_full_day()
    {
        var a = await RegisterVerifyLoginAsync("Aya");
        var b = await RegisterVerifyLoginAsync("Bana");
        // a holds 09:00; the other three slots are taken by others, so the day is full
        await BookAsync(a, "09:00:00");
        foreach (var slot in new[] { S0930, S1000, S1030 })
        {
            var filler = await C.AddPatientAsync();
            await C.AddAppointmentAsync(Doc.DoctorId, filler.PatientId, Date, slot);
        }

        var join = await SendAsync(HttpMethod.Post, $"/Appointment/patient-join-waitlist/{C.Id}/{Doc.DoctorId}", b.Token, DateStr);
        join.StatusCode.ShouldBe(HttpStatusCode.OK, await join.Content.ReadAsStringAsync());

        var cancel = await PostAsync($"/Appointment/patient-cancel-appointment/{C.Id}/{Doc.DoctorId}",
            new { timeSlot = "09:00:00", date = DateStr }, a.Token);
        cancel.StatusCode.ShouldBe(HttpStatusCode.OK, await cancel.Content.ReadAsStringAsync());

        await using var db = Db();
        var bAppt = await db.Appointments.SingleAsync(x => x.patientId == b.PatientId && x.appointmentStatus != AppointmentStatus.Cancelled);
        bAppt.timeSlot.ShouldBe(S0900);
        bAppt.appointmentStatus.ShouldBe(AppointmentStatus.Confirmed);

        var entry = await db.Waitlists.SingleAsync(w => w.PatientId == b.PatientId);
        entry.Status.ShouldBe(WaitlistStatus.Entered);
        entry.PromotedAppointmentId.ShouldBe(bAppt.Id);

        (await db.Notifications.AnyAsync(n => n.UserId == b.UserId)).ShouldBeTrue();
    }

    [Fact]
    public async Task Joining_the_waitlist_while_slots_are_free_is_refused_and_twice_is_refused()
    {
        var p = await RegisterVerifyLoginAsync();
        var refused = await SendAsync(HttpMethod.Post, $"/Appointment/patient-join-waitlist/{C.Id}/{Doc.DoctorId}", p.Token, DateStr);
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        await FillTheDayAsync();
        (await SendAsync(HttpMethod.Post, $"/Appointment/patient-join-waitlist/{C.Id}/{Doc.DoctorId}", p.Token, DateStr))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Post, $"/Appointment/patient-join-waitlist/{C.Id}/{Doc.DoctorId}", p.Token, DateStr))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        await using var db = Db();
        (await db.Waitlists.CountAsync(w => w.PatientId == p.PatientId)).ShouldBe(1);
    }

    [Fact]
    public async Task Reschedule_moves_the_booking_and_keeps_a_single_active_appointment()
    {
        var p = await RegisterVerifyLoginAsync();
        var appt = await BookAsync(p, "09:00:00");

        var res = await PostAsync($"/Appointment/patient-reschedule-appointment/{C.Id}/{Doc.DoctorId}",
            new { appointmentId = appt, timeSlot = "10:00:00", date = DateStr }, p.Token);
        (await OkJsonAsync(res)).GetProperty("success").GetBoolean().ShouldBeTrue();

        var free = await FreeSlotsAsync();
        free.ShouldContain("09:00");
        free.ShouldNotContain("10:00");

        await using var db = Db();
        var active = await db.Appointments
            .Where(a => a.patientId == p.PatientId && (a.appointmentStatus == AppointmentStatus.Confirmed || a.appointmentStatus == AppointmentStatus.Pending))
            .ToListAsync();
        active.Count.ShouldBe(1);
        active[0].timeSlot.ShouldBe(S1000);
    }

    [Fact]
    public async Task A_patient_cannot_reschedule_or_cancel_someone_elses_appointment()
    {
        var a = await RegisterVerifyLoginAsync("Aya");
        var b = await RegisterVerifyLoginAsync("Bana");
        var appt = await BookAsync(a, "09:00:00");

        var move = await PostAsync($"/Appointment/patient-reschedule-appointment/{C.Id}/{Doc.DoctorId}",
            new { appointmentId = appt, timeSlot = "10:00:00", date = DateStr }, b.Token);
        ((int)move.StatusCode).ShouldBeInRange(400, 499);

        var cancel = await PostAsync($"/Appointment/patient-cancel-appointment/{C.Id}/{Doc.DoctorId}",
            new { timeSlot = "09:00:00", date = DateStr }, b.Token);
        ((int)cancel.StatusCode).ShouldBeInRange(400, 499);

        await using var db = Db();
        var row = await db.Appointments.SingleAsync(x => x.Id == appt);
        row.timeSlot.ShouldBe(S0900);
        row.appointmentStatus.ShouldBe(AppointmentStatus.Confirmed);
    }

    // ---------- sub-patients ----------

    private object NewChild(string first) => new
    {
        subPatients = new[]
        {
            new { subPatientFirstName = first, subPatientLastName = "Sami", bloodType = "A_Positive",
                  whatAppNumber = "0591234567", gender = "Male", dateOfBith = "2018-05-05" }
        }
    };

    [Fact]
    public async Task A_parent_adds_a_child_and_books_for_them()
    {
        var p = await RegisterVerifyLoginAsync();
        var add = await PostAsync($"/SubPatient/patient-add-sub-patient/{C.Id}", NewChild("Omar"), p.Token);
        add.StatusCode.ShouldBe(HttpStatusCode.OK, await add.Content.ReadAsStringAsync());

        await using var db = Db();
        var child = await db.Patients.SingleAsync(x => x.ParentPatientId == p.PatientId);

        var appt = await BookAsync(p, "09:30:00", subPatientId: child.patientId);

        var dash = await GetAsync(
            $"/AppointmentDashboard/patient-appointment-dashboard/{C.Id}?date={DateStr}&subPatientId={child.patientId}&PageNumber=1&PageSize=10&status=Confirmed", p.Token);
        var json = await OkJsonAsync(dash);
        json.GetProperty("items").GetArrayLength().ShouldBe(1);
        json.GetProperty("items")[0].GetProperty("appointmentId").GetInt32().ShouldBe(appt);

        // the parent's own list is empty
        var own = await GetAsync(
            $"/AppointmentDashboard/patient-appointment-dashboard/{C.Id}?date={DateStr}&PageNumber=1&PageSize=10&status=Confirmed", p.Token);
        (await OkJsonAsync(own)).GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Booking_for_another_familys_child_is_refused()
    {
        var parent = await RegisterVerifyLoginAsync("Parent");
        var stranger = await RegisterVerifyLoginAsync("Stranger");
        (await PostAsync($"/SubPatient/patient-add-sub-patient/{C.Id}", NewChild("Omar"), parent.Token))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var db = Db();
        var child = await db.Patients.SingleAsync(x => x.ParentPatientId == parent.PatientId);

        var res = await BookRaw(stranger.Token, "09:00:00", subPatientId: child.patientId);

        ((int)res.StatusCode).ShouldBeInRange(400, 499);
        (await db.Appointments.CountAsync(a => a.patientId == child.patientId)).ShouldBe(0);
    }
}
