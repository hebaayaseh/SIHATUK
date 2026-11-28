using System.Net;
using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Domain.Entities;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.PaymentEnums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Api;

/// <summary>
/// E2E batch 2 (part 2): everything around the visit itself. Receptionist desk (book/cancel/reschedule for a patient,
/// waitlist entry), the three "day views" (receptionist, doctor, admin summary) and editing the bill.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class ReceptionDeskFlowTests(SqlServerFixture fx) : FlowTestBase(fx)
{
    private const string Marker = "desk-diagnosis-3317";
    private static DateOnly Today => ClinicClock.Today;
    private int _receptionistId;
    private string RecToken => ReceptionistToken(_receptionistId);

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _receptionistId = await C.AddReceptionistAsync();
        await AddVisitPriceAsync();
    }

    private string PatientToken(PatientInfo p) => Token("Patient", C.Id, p.UserId);
    private static async Task Ok(HttpResponseMessage r) => r.StatusCode.ShouldBe(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
    private static void Rejected(HttpResponseMessage r) => ((int)r.StatusCode).ShouldBeInRange(400, 499);

    private Task<HttpResponseMessage> RecBook(int patientId, string slot, string? token = null, string? date = null) =>
        PostAsync($"/Appointment/receptionist-book-appointment/{C.Id}/{Doc.DoctorId}",
            new { patientId, timeSlot = slot, dateOnly = date ?? DateStr }, token ?? RecToken);

    private async Task<int> AddOtherItemAsync(decimal price)
    {
        await using var db = Db();
        var sp = new ServicePrice { ServiceName = "Extra", Price = price, Type = ServiceType.Other };
        db.ServicePrices.Add(sp);
        await db.SaveChangesAsync();
        return sp.Id;
    }

    // ---------- receptionist books / cancels / reschedules for a patient ----------

    [Fact]
    public async Task The_receptionist_books_for_a_patient_who_then_sees_it_and_the_slot_is_gone()
    {
        var p = await C.AddPatientAsync();

        var json = await OkJsonAsync(await RecBook(p.PatientId, "09:30:00"));
        json.GetProperty("success").GetBoolean().ShouldBeTrue(json.ToString());

        (await FreeSlotsAsync()).ShouldBe(new[] { "09:00", "10:00", "10:30" });

        var dash = await OkJsonAsync(await GetAsync(
            $"/AppointmentDashboard/patient-appointment-dashboard/{C.Id}?date={DateStr}&PageNumber=1&PageSize=10&status=Confirmed", PatientToken(p)));
        dash.GetProperty("items").GetArrayLength().ShouldBe(1);
        dash.GetProperty("items")[0].GetProperty("timeSlot").GetString().ShouldStartWith("09:30");
    }

    [Fact]
    public async Task The_receptionist_cannot_book_a_taken_slot_a_past_date_or_without_the_right_role()
    {
        var a = await C.AddPatientAsync();
        var b = await C.AddPatientAsync();
        await C.AddAppointmentAsync(Doc.DoctorId, a.PatientId, Date, S0900);

        var taken = await OkJsonAsync(await RecBook(b.PatientId, "09:00:00"));
        taken.GetProperty("success").GetBoolean().ShouldBeFalse();

        (await RecBook(b.PatientId, "10:00:00", date: Date.AddDays(-30).ToString("yyyy-MM-dd"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await RecBook(b.PatientId, "10:00:00", token: PatientToken(b))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await RecBook(b.PatientId, "10:00:00", token: DoctorToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await RecBook(99999, "10:00:00")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        await using var db = Db();
        (await db.Appointments.CountAsync(x => x.patientId == b.PatientId)).ShouldBe(0);
    }

    [Fact]
    public async Task The_receptionist_cancels_and_reschedules_for_a_patient()
    {
        var p = await C.AddPatientAsync();
        var appt = await C.AddAppointmentAsync(Doc.DoctorId, p.PatientId, Date, S0900);

        var move = await PostAsync($"/Appointment/receptionist-reschedule-appointment/{C.Id}/{Doc.DoctorId}",
            new { patientId = p.PatientId, appointmentId = appt, timeSlot = "10:00:00", date = DateStr }, RecToken);
        (await OkJsonAsync(move)).GetProperty("success").GetBoolean().ShouldBeTrue();
        (await FreeSlotsAsync()).ShouldContain("09:00");

        var cancel = await PostAsync($"/Appointment/receptionist-cancel-appointment/{C.Id}/{Doc.DoctorId}",
            new { patientId = p.PatientId, timeSlot = "10:00:00", date = DateStr, resone = "patient called" }, RecToken);
        await Ok(cancel);

        (await FreeSlotsAsync()).ShouldBe(new[] { "09:00", "09:30", "10:00", "10:30" });
        await using var db = Db();
        (await db.Appointments.Where(a => a.patientId == p.PatientId && a.appointmentStatus != AppointmentStatus.Cancelled
                                          && a.appointmentStatus != AppointmentStatus.Rescheduled).CountAsync()).ShouldBe(0);
    }

    // ---------- waitlist handled by the receptionist (all over HTTP) ----------

    [Fact]
    public async Task A_waiting_patient_is_entered_by_the_receptionist_after_the_last_slot_when_the_day_is_full()
    {
        foreach (var slot in new[] { S0900, S0930, S1000, S1030 })
        {
            var f = await C.AddPatientAsync();
            await C.AddAppointmentAsync(Doc.DoctorId, f.PatientId, Date, slot);
        }
        var waiting = await C.AddPatientAsync();
        await Ok(await SendAsync(HttpMethod.Post, $"/Appointment/patient-join-waitlist/{C.Id}/{Doc.DoctorId}", PatientToken(waiting), DateStr));

        var list = await OkJsonAsync(await GetAsync(
            $"/Appointment/receptionist-view-waitlist/{C.Id}/{Doc.DoctorId}?date={DateStr}&PageNumber=1&PageSize=10", RecToken));
        list.GetProperty("items").GetArrayLength().ShouldBe(1);

        int waitListId;
        await using (var db0 = Db()) waitListId = await db0.Waitlists.Where(w => w.PatientId == waiting.PatientId).Select(w => w.Id).SingleAsync();

        (await SendAsync(HttpMethod.Post, $"/Appointment/receptionist-enter-from-waitlist/{C.Id}/{Doc.DoctorId}", PatientToken(waiting), new { waitListId }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var entered = await OkJsonAsync(await SendAsync(HttpMethod.Post,
            $"/Appointment/receptionist-enter-from-waitlist/{C.Id}/{Doc.DoctorId}", RecToken, new { waitListId }));
        entered.GetProperty("success").GetBoolean().ShouldBeTrue(entered.ToString());
        entered.GetProperty("isOverflow").GetBoolean().ShouldBeTrue();
        entered.GetProperty("timeSlot").GetString().ShouldStartWith("11:00");

        await using var db = Db();
        (await db.Waitlists.SingleAsync(w => w.Id == waitListId)).Status.ShouldBe(WaitlistStatus.Entered);
        (await db.Appointments.SingleAsync(a => a.patientId == waiting.PatientId)).appointmentStatus.ShouldBe(AppointmentStatus.Confirmed);

        // entering twice must not create a second appointment
        Rejected(await SendAsync(HttpMethod.Post, $"/Appointment/receptionist-enter-from-waitlist/{C.Id}/{Doc.DoctorId}", RecToken, new { waitListId }));
        (await db.Appointments.CountAsync(a => a.patientId == waiting.PatientId)).ShouldBe(1);

        var mine = await OkJsonAsync(await GetAsync(
            $"/AppointmentDashboard/patient-appointment-dashboard/{C.Id}?date={DateStr}&PageNumber=1&PageSize=10&status=Confirmed", PatientToken(waiting)));
        mine.GetProperty("items").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task The_receptionist_can_remove_a_waiting_patient_who_then_cannot_be_entered()
    {
        foreach (var slot in new[] { S0900, S0930, S1000, S1030 })
        {
            var f = await C.AddPatientAsync();
            await C.AddAppointmentAsync(Doc.DoctorId, f.PatientId, Date, slot);
        }
        var waiting = await C.AddPatientAsync();
        await Ok(await SendAsync(HttpMethod.Post, $"/Appointment/patient-join-waitlist/{C.Id}/{Doc.DoctorId}", PatientToken(waiting), DateStr));
        int waitListId;
        await using (var db0 = Db()) waitListId = await db0.Waitlists.Where(w => w.PatientId == waiting.PatientId).Select(w => w.Id).SingleAsync();

        await Ok(await SendAsync(HttpMethod.Delete, $"/Appointment/receptionist-remove-from-waitlist/{C.Id}/{Doc.DoctorId}/{waitListId}", RecToken));

        Rejected(await SendAsync(HttpMethod.Post, $"/Appointment/receptionist-enter-from-waitlist/{C.Id}/{Doc.DoctorId}", RecToken, new { waitListId }));
        await using var db = Db();
        (await db.Waitlists.SingleAsync(w => w.Id == waitListId)).Status.ShouldNotBe(WaitlistStatus.Waiting);
        (await db.Appointments.CountAsync(a => a.patientId == waiting.PatientId)).ShouldBe(0);
    }

    // ---------- the day views ----------

    [Fact]
    public async Task The_receptionist_dashboard_follows_a_visit_from_confirmed_to_paid()
    {
        var p = await C.AddPatientAsync();
        var appt = await C.AddAppointmentAsync(Doc.DoctorId, p.PatientId, Today, S0900);
        string Url() => $"/AppointmentDashboard/receptionist-appointment-dashboard/{C.Id}?date={Today:yyyy-MM-dd}&PageNumber=1&PageSize=50";
        async Task<System.Text.Json.JsonElement> Row() =>
            (await OkJsonAsync(await GetAsync(Url(), RecToken))).GetProperty("items").EnumerateArray()
                .Single(e => e.GetProperty("appointmentId").GetInt32() == appt);

        (await Row()).GetProperty("appointmentStatus").GetString().ShouldBe("Confirmed");

        await Ok(await SendAsync(HttpMethod.Put, $"/CheckTime/receptionist-checkIn-patient/{C.Id}/{appt}", RecToken));
        (await Row()).GetProperty("checkInTime").ValueKind.ShouldNotBe(System.Text.Json.JsonValueKind.Null);

        await Ok(await SendAsync(HttpMethod.Put, $"/CheckTime/receptionist-next-patient/{C.Id}/{appt}", RecToken));
        (await Row()).GetProperty("appointmentStatus").GetString().ShouldBe("InProgress");

        await Ok(await PostAsync($"/MedicalRecord/doctor-add-medicalrecord/{C.Id}",
            new { patientId = p.PatientId, appointmentId = appt, diagnosis = Marker, prescription = "r", notes = "n" }, DoctorToken));
        var billed = await Row();
        billed.GetProperty("billAmount").GetDecimal().ShouldBe(50m);
        billed.GetProperty("paymentStatus").GetString().ShouldBe("Pending");

        await Ok(await SendAsync(HttpMethod.Put, $"/CheckTime/doctor-finish-appointment-time/{C.Id}/{appt}", DoctorToken));
        await Ok(await SendAsync(HttpMethod.Post, $"/ConfirmPayment/receptionist-appointment-paid/{C.Id}/{appt}", RecToken, new { method = 2 }));
        var paid = await Row();
        paid.GetProperty("paymentStatus").GetString().ShouldBe("Paid");
        paid.GetProperty("appointmentStatus").GetString().ShouldBe("Completed");

        await using var db = Db();
        (await db.Payments.SingleAsync(x => x.AppointmentId == appt)).Method.ShouldBe(PaymentMethod.card);
    }

    [Fact]
    public async Task The_receptionist_dashboard_is_for_receptionists_only()
    {
        var url = $"/AppointmentDashboard/receptionist-appointment-dashboard/{C.Id}?date={DateStr}&PageNumber=1&PageSize=10";
        (await GetAsync(url)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await GetAsync(url, DoctorToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await GetAsync(url, Token("Patient", C.Id, 1))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await GetAsync(url, RecToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_doctor_sees_all_their_patients_of_the_day_except_cancelled_in_slot_order()
    {
        var other = await C.AddDoctorAsync();
        async Task<(int Id, PatientInfo P)> Seed(DoctorInfo d, TimeOnly slot, AppointmentStatus s)
        {
            var p = await C.AddPatientAsync();
            return (await C.AddAppointmentAsync(d.DoctorId, p.PatientId, Today, slot, s), p);
        }
        var noShow = await Seed(Doc, S1030, AppointmentStatus.NoShow);
        var confirmed = await Seed(Doc, S0900, AppointmentStatus.Confirmed);
        var done = await Seed(Doc, S1000, AppointmentStatus.Completed);
        var inRoom = await Seed(Doc, S0930, AppointmentStatus.InProgress);
        await Seed(Doc, new TimeOnly(11, 0), AppointmentStatus.Cancelled);
        await Seed(other, S0930, AppointmentStatus.Confirmed);                       // someone else's patient

        // the doctor writes the record for the patient who is in the room
        await Ok(await PostAsync($"/MedicalRecord/doctor-add-medicalrecord/{C.Id}",
            new { patientId = inRoom.P.PatientId, appointmentId = inRoom.Id, diagnosis = Marker, prescription = "r", notes = "n" }, DoctorToken));

        var json = await OkJsonAsync(await GetAsync($"/DoctorDailyHours/doctor-get-appointments-for-day/{C.Id}?date={Today:yyyy-MM-dd}", DoctorToken));

        var rows = json.GetProperty("appointments").EnumerateArray().ToList();
        rows.Select(e => e.GetProperty("appointmentId").GetInt32()).ShouldBe(new[] { confirmed.Id, inRoom.Id, done.Id, noShow.Id });
        rows.Select(e => e.GetProperty("status").GetString()).ShouldBe(new[] { "Confirmed", "InProgress", "Completed", "NoShow" });
        rows.Select(e => e.GetProperty("hasRecord").GetBoolean()).ShouldBe(new[] { false, true, false, false });

        (await GetAsync($"/DoctorDailyHours/doctor-get-appointments-for-day/{C.Id}?date={Today.AddDays(-3):yyyy-MM-dd}", DoctorToken))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await GetAsync($"/DoctorDailyHours/doctor-get-appointments-for-day/{C.Id}?date={Today:yyyy-MM-dd}", RecToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_admin_summary_counts_the_day_by_status_and_the_waitlist()
    {
        async Task Seed(AppointmentStatus s, TimeOnly slot) =>
            await C.AddAppointmentAsync(Doc.DoctorId, (await C.AddPatientAsync()).PatientId, Date, slot, s);
        await Seed(AppointmentStatus.Confirmed, S0900);
        await Seed(AppointmentStatus.Confirmed, S0930);
        await Seed(AppointmentStatus.Completed, S1000);
        await Seed(AppointmentStatus.Cancelled, S1030);
        await Seed(AppointmentStatus.NoShow, new TimeOnly(11, 0));
        var waiting = await C.AddPatientAsync();
        await using (var db = Db())
        {
            db.Waitlists.Add(new Waitlist { PatientId = waiting.PatientId, DoctorId = Doc.DoctorId, PreferredDate = Date, Status = WaitlistStatus.Waiting });
            await db.SaveChangesAsync();
        }

        var json = await OkJsonAsync(await GetAsync($"/AppointmentDashboard/appointments-summary/{C.Id}?date={DateStr}", AdminToken));

        json.GetProperty("total").GetInt32().ShouldBe(5);
        json.GetProperty("confirmed").GetInt32().ShouldBe(2);
        json.GetProperty("completed").GetInt32().ShouldBe(1);
        json.GetProperty("cancelled").GetInt32().ShouldBe(1);
        json.GetProperty("noShow").GetInt32().ShouldBe(1);
        json.GetProperty("waitlistCount").GetInt32().ShouldBe(1);

        (await GetAsync($"/AppointmentDashboard/appointments-summary/{C.Id}?date={DateStr}", RecToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await GetAsync($"/AppointmentDashboard/appointments-summary/{C.Id}?date={DateStr}", DoctorToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ---------- editing the bill while it is still pending ----------

    [Fact]
    public async Task The_doctor_adds_an_item_to_a_pending_bill_and_the_amount_follows()
    {
        var itemId = await AddOtherItemAsync(20m);
        var p = await C.AddPatientAsync();
        var appt = await C.AddAppointmentAsync(Doc.DoctorId, p.PatientId, Today, S0900);
        await Ok(await PostAsync($"/MedicalRecord/doctor-add-medicalrecord/{C.Id}",
            new { patientId = p.PatientId, appointmentId = appt, diagnosis = Marker, prescription = "r", notes = "n" }, DoctorToken));
        int recordId;
        await using (var db0 = Db()) recordId = await db0.MedicalRecords.Where(r => r.PatientId == p.PatientId).Select(r => r.Id).SingleAsync();

        await Ok(await PostAsync($"/MedicalRecord/doctor-update-medicalrecord/{C.Id}",
            new { patientId = p.PatientId, medicalRecordId = recordId, appointmentId = appt, items = new[] { new { id = itemId, quantity = 1 } } }, DoctorToken));

        var bill = await OkJsonAsync(await GetAsync($"/BillPayment/patient-get-appointment-bill/{C.Id}?PageNumber=1&PageSize=10", PatientToken(p)));
        bill.GetProperty("items")[0].GetProperty("amount").GetDecimal().ShouldBe(70m);
    }

    [Fact]
    public async Task Another_doctor_cannot_edit_the_record_and_the_bill_stays_as_it_was()
    {
        var itemId = await AddOtherItemAsync(20m);
        var p = await C.AddPatientAsync();
        var appt = await C.AddAppointmentAsync(Doc.DoctorId, p.PatientId, Today, S0900);
        await Ok(await PostAsync($"/MedicalRecord/doctor-add-medicalrecord/{C.Id}",
            new { patientId = p.PatientId, appointmentId = appt, diagnosis = Marker, prescription = "r", notes = "n" }, DoctorToken));
        int recordId;
        await using (var db0 = Db()) recordId = await db0.MedicalRecords.Where(r => r.PatientId == p.PatientId).Select(r => r.Id).SingleAsync();
        var stranger = await C.AddDoctorAsync();

        Rejected(await PostAsync($"/MedicalRecord/doctor-update-medicalrecord/{C.Id}",
            new { patientId = p.PatientId, medicalRecordId = recordId, appointmentId = appt, items = new[] { new { id = itemId, quantity = 3 } } },
            Token("Doctor", C.Id, stranger.UserId)));

        await using var db = Db();
        (await db.Payments.SingleAsync(x => x.AppointmentId == appt)).Amount.ShouldBe(50m);
    }
}
