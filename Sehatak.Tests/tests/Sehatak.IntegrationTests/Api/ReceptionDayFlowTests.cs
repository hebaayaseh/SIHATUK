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
/// E2E batch 2: a clinic day over real HTTP. check-in -> doctor's turn -> record (creates the pending bill) ->
/// doctor finishes -> receptionist collects -> patient sees the paid bill.
/// Today's appointments are seeded straight into the database (booking itself is covered in batch 1),
/// every step after that goes through the API.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class ReceptionDayFlowTests(SqlServerFixture fx) : FlowTestBase(fx)
{
    private const string Marker = "visit-diagnosis-5528";
    private static DateOnly Today => ClinicClock.Today;

    private int _receptionistId;
    private string RecToken => ReceptionistToken(_receptionistId);

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _receptionistId = await C.AddReceptionistAsync();
        await AddVisitPriceAsync();
    }

    private record Visit(PatientInfo Patient, int AppointmentId);

    private async Task<Visit> SeedTodayAsync(TimeOnly? slot = null, DoctorInfo? doctor = null, DateOnly? date = null,
                                             AppointmentStatus status = AppointmentStatus.Confirmed)
    {
        var p = await C.AddPatientAsync();
        var id = await C.AddAppointmentAsync((doctor ?? Doc).DoctorId, p.PatientId, date ?? Today, slot ?? S0900, status);
        return new Visit(p, id);
    }

    private Task<HttpResponseMessage> CheckIn(int appt, string? token = null) =>
        SendAsync(HttpMethod.Put, $"/CheckTime/receptionist-checkIn-patient/{C.Id}/{appt}", token ?? RecToken);
    private Task<HttpResponseMessage> Next(int appt, string? token = null) =>
        SendAsync(HttpMethod.Put, $"/CheckTime/receptionist-next-patient/{C.Id}/{appt}", token ?? RecToken);
    private Task<HttpResponseMessage> Finish(int appt, string? token = null) =>
        SendAsync(HttpMethod.Put, $"/CheckTime/doctor-finish-appointment-time/{C.Id}/{appt}", token ?? DoctorToken);
    private Task<HttpResponseMessage> Pay(int appt, string? token = null) =>
        SendAsync(HttpMethod.Post, $"/ConfirmPayment/receptionist-appointment-paid/{C.Id}/{appt}", token ?? RecToken, new { method = 1 });
    private Task<HttpResponseMessage> Record(Visit v, object? items = null, string? token = null) =>
        PostAsync($"/MedicalRecord/doctor-add-medicalrecord/{C.Id}",
            new { patientId = v.Patient.PatientId, appointmentId = v.AppointmentId, diagnosis = Marker, prescription = "rest", notes = "n", items },
            token ?? DoctorToken);

    private string PatientToken(Visit v) => Token("Patient", C.Id, v.Patient.UserId);

    private static async Task Ok(HttpResponseMessage r) => r.StatusCode.ShouldBe(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
    private static void Rejected(HttpResponseMessage r) => ((int)r.StatusCode).ShouldBeInRange(400, 499);

    // ---------- the whole day ----------

    [Fact]
    public async Task A_full_visit_from_check_in_to_a_paid_bill()
    {
        var v = await SeedTodayAsync();

        await Ok(await CheckIn(v.AppointmentId));
        await Ok(await Next(v.AppointmentId));
        await Ok(await Record(v));
        await Ok(await Finish(v.AppointmentId));
        await Ok(await Pay(v.AppointmentId));

        await using var db = Db();
        var a = await db.Appointments.SingleAsync(x => x.Id == v.AppointmentId);
        a.appointmentStatus.ShouldBe(AppointmentStatus.Completed);
        a.CheckInTime.ShouldNotBeNull();
        a.actualStartTime.ShouldNotBeNull();
        a.actualEndTime.ShouldNotBeNull();
        a.CheckOutTime.ShouldNotBeNull();
        a.BillAmount.ShouldBe(50m);

        var pay = await db.Payments.SingleAsync(p => p.AppointmentId == v.AppointmentId);
        pay.Status.ShouldBe(PaymentStatus.Paid);
        pay.Method.ShouldBe(PaymentMethod.cash);
        pay.RecordedByStaffId.ShouldBe(_receptionistId);

        var bill = await OkJsonAsync(await GetAsync($"/BillPayment/patient-get-appointment-bill/{C.Id}?PageNumber=1&PageSize=10", PatientToken(v)));
        bill.GetProperty("items").GetArrayLength().ShouldBe(1);
        bill.GetProperty("items")[0].GetProperty("billPaymentStatus").GetString().ShouldBe("Paid");
        bill.GetProperty("items")[0].GetProperty("amount").GetDecimal().ShouldBe(50m);

        var history = await GetAsync($"/MedicalRecord/patient-get-medical-history/{C.Id}?PageNumber=1&PageSize=10", PatientToken(v));
        (await history.Content.ReadAsStringAsync()).ShouldContain(Marker);
    }

    [Fact]
    public async Task Extra_items_are_added_to_the_bill_and_shown_to_the_patient()
    {
        int itemId;
        await using (var seed = Db())
        {
            var price = new ServicePrice { ServiceName = "Dressing", Price = 20m, Type = ServiceType.Other };
            seed.ServicePrices.Add(price);
            await seed.SaveChangesAsync();
            itemId = price.Id;
        }
        var v = await SeedTodayAsync();
        await Ok(await CheckIn(v.AppointmentId));
        await Ok(await Next(v.AppointmentId));

        await Ok(await Record(v, items: new[] { new { id = itemId, quantity = 2 } }));

        var bill = await OkJsonAsync(await GetAsync($"/BillPayment/patient-get-appointment-bill/{C.Id}?PageNumber=1&PageSize=10", PatientToken(v)));
        var row = bill.GetProperty("items")[0];
        row.GetProperty("billPaymentStatus").GetString().ShouldBe("Pending");
        row.GetProperty("amount").GetDecimal().ShouldBe(90m);                       // 50 visit + 2 x 20
        row.GetProperty("items").GetArrayLength().ShouldBe(1);
        row.GetProperty("items")[0].GetProperty("quantity").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task Another_patient_never_sees_the_bill()
    {
        var v = await SeedTodayAsync();
        await Ok(await CheckIn(v.AppointmentId));
        await Ok(await Next(v.AppointmentId));
        await Ok(await Record(v));

        var other = await C.AddPatientAsync();
        var bill = await OkJsonAsync(await GetAsync($"/BillPayment/patient-get-appointment-bill/{C.Id}?PageNumber=1&PageSize=10",
            Token("Patient", C.Id, other.UserId)));

        bill.GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Any_doctor_of_the_center_opens_the_record_by_id_by_design_and_another_center_cannot()
    {
        var v = await SeedTodayAsync();
        await Ok(await CheckIn(v.AppointmentId));
        await Ok(await Next(v.AppointmentId));
        await Ok(await Record(v));
        await using var db = Db();
        var recordId = await db.MedicalRecords.Where(r => r.PatientId == v.Patient.PatientId).Select(r => r.Id).SingleAsync();

        var colleague = await C.AddDoctorAsync();
        var seen = await GetAsync($"/MedicalRecord/doctor-record/{C.Id}/{recordId}", Token("Doctor", C.Id, colleague.UserId));
        seen.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await seen.Content.ReadAsStringAsync()).ShouldContain(Marker);

        var other = await Fx.Harness.CreateCenterAsync();
        try
        {
            var foreign = await other.AddDoctorAsync();
            var res = await GetAsync($"/MedicalRecord/doctor-record/{C.Id}/{recordId}", Token("Doctor", other.Id, foreign.UserId));
            ((int)res.StatusCode).ShouldBeInRange(401, 403);
            (await res.Content.ReadAsStringAsync()).ShouldNotContain(Marker);
        }
        finally { await Fx.Harness.DropTenantAsync(other.Id); }
    }

    // ---------- check-in ----------

    [Fact]
    public async Task Check_in_twice_or_for_another_day_is_refused()
    {
        var v = await SeedTodayAsync();
        var tomorrow = await SeedTodayAsync(S0930, date: Today.AddDays(1));

        await Ok(await CheckIn(v.AppointmentId));
        Rejected(await CheckIn(v.AppointmentId));
        Rejected(await CheckIn(tomorrow.AppointmentId));

        await using var db = Db();
        (await db.Appointments.SingleAsync(a => a.Id == tomorrow.AppointmentId)).CheckInTime.ShouldBeNull();
    }

    [Fact]
    public async Task Check_in_of_a_cancelled_appointment_is_refused()
    {
        var v = await SeedTodayAsync(status: AppointmentStatus.Cancelled);
        Rejected(await CheckIn(v.AppointmentId));
    }

    [Fact]
    public async Task Only_a_receptionist_can_check_in_start_and_collect()
    {
        var v = await SeedTodayAsync();

        (await CheckIn(v.AppointmentId, PatientToken(v))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CheckIn(v.AppointmentId, DoctorToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Next(v.AppointmentId, DoctorToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Pay(v.AppointmentId, DoctorToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Pay(v.AppointmentId, PatientToken(v))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Finish(v.AppointmentId, RecToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Put, $"/CheckTime/receptionist-checkIn-patient/{C.Id}/{v.AppointmentId}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        await using var db = Db();
        var a = await db.Appointments.SingleAsync(x => x.Id == v.AppointmentId);
        a.CheckInTime.ShouldBeNull();
        a.appointmentStatus.ShouldBe(AppointmentStatus.Confirmed);
    }

    // ---------- doctor's turn ----------

    [Fact]
    public async Task The_doctor_sees_one_patient_at_a_time()
    {
        var first = await SeedTodayAsync(S0900);
        var second = await SeedTodayAsync(S0930);

        await Ok(await Next(first.AppointmentId));
        Rejected(await Next(second.AppointmentId));                                  // doctor is busy
        Rejected(await Next(first.AppointmentId));                                   // already started

        await Ok(await Finish(first.AppointmentId));
        await Ok(await Next(second.AppointmentId));                                  // free again

        await using var db = Db();
        (await db.Appointments.SingleAsync(a => a.Id == second.AppointmentId)).appointmentStatus.ShouldBe(AppointmentStatus.InProgress);
    }

    [Fact]
    public async Task Two_doctors_can_each_have_a_patient_in_the_room()
    {
        var other = await C.AddDoctorAsync();
        var a = await SeedTodayAsync(S0900);
        var b = await SeedTodayAsync(S0900, doctor: other);

        await Ok(await Next(a.AppointmentId));
        await Ok(await Next(b.AppointmentId));
    }

    [Fact]
    public async Task Finishing_needs_the_right_doctor_a_started_visit_and_happens_once()
    {
        var v = await SeedTodayAsync();
        var stranger = await C.AddDoctorAsync();

        Rejected(await Finish(v.AppointmentId));                                     // not started yet
        await Ok(await Next(v.AppointmentId));
        Rejected(await Finish(v.AppointmentId, Token("Doctor", C.Id, stranger.UserId)));   // someone else's patient
        await Ok(await Finish(v.AppointmentId));
        Rejected(await Finish(v.AppointmentId));                                     // already finished

        await using var db = Db();
        (await db.Appointments.SingleAsync(a => a.Id == v.AppointmentId)).appointmentStatus.ShouldBe(AppointmentStatus.Completed);
    }

    // ---------- medical record guard rails ----------

    [Fact]
    public async Task One_record_per_appointment_and_not_for_a_cancelled_one()
    {
        var v = await SeedTodayAsync();
        var cancelled = await SeedTodayAsync(S0930, status: AppointmentStatus.Cancelled);

        await Ok(await Record(v));
        Rejected(await Record(v));
        Rejected(await Record(cancelled));

        await using var db = Db();
        (await db.MedicalRecords.CountAsync(r => r.PatientId == v.Patient.PatientId)).ShouldBe(1);
        (await db.Payments.CountAsync(p => p.AppointmentId == v.AppointmentId)).ShouldBe(1);
        (await db.Payments.CountAsync(p => p.AppointmentId == cancelled.AppointmentId)).ShouldBe(0);
    }

    [Fact]
    public async Task A_doctor_cannot_write_a_record_on_another_doctors_appointment()
    {
        var v = await SeedTodayAsync();
        var stranger = await C.AddDoctorAsync();

        Rejected(await Record(v, token: Token("Doctor", C.Id, stranger.UserId)));

        await using var db = Db();
        (await db.MedicalRecords.CountAsync(r => r.PatientId == v.Patient.PatientId)).ShouldBe(0);
        (await db.Payments.CountAsync(p => p.AppointmentId == v.AppointmentId)).ShouldBe(0);
    }

    [Fact]
    public async Task A_record_must_link_to_an_appointment_or_consultation_and_only_a_doctor_writes_it()
    {
        var v = await SeedTodayAsync();

        var none = await PostAsync($"/MedicalRecord/doctor-add-medicalrecord/{C.Id}",
            new { patientId = v.Patient.PatientId, diagnosis = Marker, prescription = "r", notes = "n" }, DoctorToken);
        Rejected(none);

        (await Record(v, token: PatientToken(v))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Record(v, token: RecToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ---------- collecting the money ----------

    [Fact]
    public async Task Payment_cannot_be_collected_before_the_bill_exists_or_the_visit_started()
    {
        var v = await SeedTodayAsync();

        Rejected(await Pay(v.AppointmentId));                                        // no bill yet

        await Ok(await Record(v));                                                   // bill exists, visit not started
        Rejected(await Pay(v.AppointmentId));

        await using var db = Db();
        (await db.Payments.SingleAsync(p => p.AppointmentId == v.AppointmentId)).Status.ShouldBe(PaymentStatus.Pending);
    }

    [Fact]
    public async Task Collecting_twice_pays_once()
    {
        var v = await SeedTodayAsync();
        await Ok(await Next(v.AppointmentId));
        await Ok(await Record(v));
        await Ok(await Finish(v.AppointmentId));

        await Ok(await Pay(v.AppointmentId));
        Rejected(await Pay(v.AppointmentId));

        await using var db = Db();
        (await db.Payments.CountAsync(p => p.AppointmentId == v.AppointmentId && p.Status == PaymentStatus.Paid)).ShouldBe(1);
    }

    [Fact]
    public async Task Bad_payment_bodies_are_400_never_500()
    {
        var v = await SeedTodayAsync();
        await Ok(await Next(v.AppointmentId));
        await Ok(await Record(v));

        var bad = await SendAsync(HttpMethod.Post, $"/ConfirmPayment/receptionist-appointment-paid/{C.Id}/{v.AppointmentId}", RecToken, new { method = 99 });
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var missing = await SendAsync(HttpMethod.Post, $"/ConfirmPayment/receptionist-appointment-paid/{C.Id}/999999", RecToken, new { method = 1 });
        missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        await using var db = Db();
        (await db.Payments.SingleAsync(p => p.AppointmentId == v.AppointmentId)).Status.ShouldBe(PaymentStatus.Pending);
    }
}
