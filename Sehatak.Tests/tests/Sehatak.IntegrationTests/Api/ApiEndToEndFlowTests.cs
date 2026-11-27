using System.Net;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Api;

/// <summary>
/// A whole story through the REAL HTTP pipeline: patient registers -> verifies the e-mail code -> logs in -> books ->
/// the doctor writes the record -> the patient reads it. Service tests prove each piece; this proves they are wired together.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class ApiEndToEndFlowTests(SqlServerFixture fx) : FlowTestBase(fx)
{
    private const string Marker = "e2e-diagnosis-7391";

    private Task<HttpResponseMessage> DoctorWritesRecordAsync(FlowPatient p, int appointmentId, int? doctorUserId = null) =>
        PostAsync($"/MedicalRecord/doctor-add-medicalrecord/{C.Id}",
            new { patientId = p.PatientId, appointmentId, diagnosis = Marker, prescription = "rest", notes = "n" },
            Token("Doctor", C.Id, doctorUserId ?? Doc.UserId));

    [Fact]
    public async Task A_patient_registers_books_and_reads_what_the_doctor_wrote()
    {
        await AddVisitPriceAsync();
        var p = await RegisterVerifyLoginAsync();
        var appt = await BookAsync(p);

        var write = await DoctorWritesRecordAsync(p, appt);
        write.StatusCode.ShouldBe(HttpStatusCode.OK, await write.Content.ReadAsStringAsync());

        var history = await GetAsync($"/MedicalRecord/patient-get-medical-history/{C.Id}?PageNumber=1&PageSize=10", p.Token);
        history.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await history.Content.ReadAsStringAsync()).ShouldContain(Marker);
    }

    [Fact]
    public async Task Another_patient_never_sees_that_record()
    {
        await AddVisitPriceAsync();
        var first = await RegisterVerifyLoginAsync();
        var appt = await BookAsync(first);
        (await DoctorWritesRecordAsync(first, appt)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var second = await RegisterVerifyLoginAsync();
        var history = await GetAsync($"/MedicalRecord/patient-get-medical-history/{C.Id}?PageNumber=1&PageSize=10", second.Token);

        (await history.Content.ReadAsStringAsync()).ShouldNotContain(Marker);
    }

    [Fact]
    public async Task The_treating_doctor_can_open_the_patient_history_over_http()
    {
        await AddVisitPriceAsync();
        var p = await RegisterVerifyLoginAsync();
        var appt = await BookAsync(p);
        (await DoctorWritesRecordAsync(p, appt)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var res = await GetAsync($"/MedicalRecord/doctor-patient-history/{C.Id}/{p.PatientId}?PageNumber=1&PageSize=10", DoctorToken);

        res.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await res.Content.ReadAsStringAsync()).ShouldContain(Marker);
    }

    [Fact]
    public async Task Any_doctor_of_the_center_can_open_the_patient_history_by_design()
    {
        await AddVisitPriceAsync();
        var p = await RegisterVerifyLoginAsync();
        var appt = await BookAsync(p);
        (await DoctorWritesRecordAsync(p, appt)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var colleague = await C.AddDoctorAsync();   // never treated this patient

        var res = await GetAsync($"/MedicalRecord/doctor-patient-history/{C.Id}/{p.PatientId}?PageNumber=1&PageSize=10",
            Token("Doctor", C.Id, colleague.UserId));

        res.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await res.Content.ReadAsStringAsync()).ShouldContain(Marker);
    }

    [Fact]
    public async Task A_doctor_of_another_center_cannot_open_the_history()
    {
        await AddVisitPriceAsync();
        var p = await RegisterVerifyLoginAsync();
        var appt = await BookAsync(p);
        (await DoctorWritesRecordAsync(p, appt)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var other = await Fx.Harness.CreateCenterAsync();
        try
        {
            var foreignDoctor = await other.AddDoctorAsync();
            var res = await GetAsync($"/MedicalRecord/doctor-patient-history/{C.Id}/{p.PatientId}?PageNumber=1&PageSize=10",
                Token("Doctor", other.Id, foreignDoctor.UserId));

            ((int)res.StatusCode).ShouldBeInRange(401, 403);
            (await res.Content.ReadAsStringAsync()).ShouldNotContain(Marker);
        }
        finally { await Fx.Harness.DropTenantAsync(other.Id); }
    }

    [Fact]
    public async Task A_patient_who_deactivates_the_profile_cannot_log_in_again()
    {
        var p = await RegisterVerifyLoginAsync();

        (await SendAsync(HttpMethod.Put, $"/Auth/patient-deactive-profile/{C.Id}", p.Token)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var login = await PostAsync($"/Auth/patient-login-patient/{C.Id}", new { email = p.Email, password = Pwd });
        ((int)login.StatusCode).ShouldBeInRange(400, 499);
    }
}
