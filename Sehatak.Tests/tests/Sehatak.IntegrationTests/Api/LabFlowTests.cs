using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sehatak.Domain.Entities;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.PaymentEnums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Api;

/// <summary>
/// E2E batch 3: the lab journey over real HTTP.
/// doctor/receptionist request -> technician collects the sample -> receptionist collects the money ->
/// technician uploads results -> patient reads results and bill.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class LabFlowTests(SqlServerFixture fx) : FlowTestBase(fx)
{
    private int _receptionistId;
    private int _technicianId;
    private string RecToken => ReceptionistToken(_receptionistId);
    private string TechToken => Token("LabTechnician", C.Id, _technicianId);
    private string PatientToken(PatientInfo p) => Token("Patient", C.Id, p.UserId);

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _receptionistId = await C.AddReceptionistAsync();
        _technicianId = await C.AddLabTechnicianAsync();
    }

    private static async Task Ok(HttpResponseMessage r) => r.StatusCode.ShouldBe(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
    private static void Rejected(HttpResponseMessage r) => ((int)r.StatusCode).ShouldBeInRange(400, 499);

    private async Task<int> AddLabPriceAsync(string name, decimal price, ServiceType type = ServiceType.LabTest)
    {
        await using var db = Db();
        var sp = new ServicePrice { ServiceName = name, Price = price, Type = type };
        db.ServicePrices.Add(sp);
        await db.SaveChangesAsync();
        return sp.Id;
    }

    private record LabCase(PatientInfo Patient, int AppointmentId, int LabId, List<int> ItemIds);

    private async Task<(PatientInfo P, int Appt)> PatientWithVisitAsync()
    {
        var p = await C.AddPatientAsync();
        return (p, await C.AddAppointmentAsync(Doc.DoctorId, p.PatientId, Date, S0900));
    }

    private async Task<List<int>> ItemIdsAsync(int labId)
    {
        await using var db = Db();
        return await db.LabRequestItems.Where(i => i.LabRequestId == labId).OrderBy(i => i.Id).Select(i => i.Id).ToListAsync();
    }

    private async Task<LabCase> DoctorRequestsAsync(params int[] priceIds)
    {
        var (p, appt) = await PatientWithVisitAsync();
        var res = await PostAsync($"/Lab/doctor-create-lab-request/{C.Id}",
            new { patientId = p.PatientId, appointmentId = appt, note = "fasting", labItems = priceIds.Select(i => new { id = i }) }, DoctorToken);
        var labId = (await OkJsonAsync(res)).GetProperty("labRequestId").GetInt32();
        return new LabCase(p, appt, labId, await ItemIdsAsync(labId));
    }

    private Task<HttpResponseMessage> Collect(int labId, int[]? unavailable = null, string? token = null) =>
        SendAsync(HttpMethod.Put, $"/Lab/technician-collected-lab-request/{C.Id}/{labId}", token ?? TechToken, unavailable ?? Array.Empty<int>());

    private Task<HttpResponseMessage> Pay(int labId, string? token = null) =>
        SendAsync(HttpMethod.Post, $"/ConfirmPayment/receptionist-lab-paid/{C.Id}/{labId}", token ?? RecToken, new { method = 1 });

    private Task<HttpResponseMessage> Upload(int labId, IEnumerable<(int ItemId, decimal Value)> results, string? token = null,
        (string Name, string ContentType)? file = null)
    {
        var form = new MultipartFormDataContent { { new StringContent(labId.ToString()), "LabRequestId" } };
        var i = 0;
        foreach (var (itemId, value) in results)
        {
            form.Add(new StringContent(itemId.ToString()), $"Results[{i}].LabRequestItemId");
            form.Add(new StringContent(value.ToString(CultureInfo.InvariantCulture)), $"Results[{i}].ResultValue");
            if (file != null)
            {
                var bytes = new ByteArrayContent("%PDF-1.4\n1 0 obj\n<<>>\nendobj\n"u8.ToArray());
                bytes.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(file.Value.ContentType);
                form.Add(bytes, $"Results[{i}].ResultFileUrl", file.Value.Name);
            }
            i++;
        }
        var req = new HttpRequestMessage(HttpMethod.Put, $"/Lab/technician-uplod-lab-request/{C.Id}") { Content = form };
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token ?? TechToken);
        return Http.SendAsync(req);
    }

    private async Task<LabRequestStatus> StatusAsync(int labId)
    {
        await using var db = Db();
        return await db.LabRequests.Where(l => l.Id == labId).Select(l => l.Status).SingleAsync();
    }

    // ---------- the whole journey ----------

    [Fact]
    public async Task Doctor_request_to_results_in_the_patients_hands()
    {
        var cbc = await AddLabPriceAsync("CBC", 30m);
        var sugar = await AddLabPriceAsync("Sugar", 20m);
        var lab = await DoctorRequestsAsync(cbc, sugar);

        // technician sees it waiting
        var pending = await OkJsonAsync(await GetAsync($"/Lab/technician-get-lab-pending-requests/{C.Id}?PageNumber=1&PageSize=10", TechToken));
        pending.GetProperty("items").EnumerateArray().Any(e => e.GetProperty("labRequestId").GetInt32() == lab.LabId).ShouldBeTrue();

        await Ok(await Collect(lab.LabId));
        (await StatusAsync(lab.LabId)).ShouldBe(LabRequestStatus.Collected);

        // receptionist sees it awaiting payment with the right total
        var awaiting = await OkJsonAsync(await GetAsync($"/Lab/receptionist-get-lab-requests-await-payment/{C.Id}?PageNumber=1&PageSize=10", RecToken));
        var row = awaiting.GetProperty("items").EnumerateArray().Single(e => e.GetProperty("labRequestId").GetInt32() == lab.LabId);
        row.GetProperty("totalPrice").GetDecimal().ShouldBe(50m);

        await Ok(await Pay(lab.LabId));
        (await StatusAsync(lab.LabId)).ShouldBe(LabRequestStatus.Processing);

        var bill = await OkJsonAsync(await GetAsync($"/BillPayment/patient-get-lab-bill/{C.Id}?PageNumber=1&PageSize=10", PatientToken(lab.Patient)));
        bill.GetProperty("items").GetArrayLength().ShouldBe(1);
        bill.GetProperty("items")[0].GetProperty("billPaymentStatus").GetString().ShouldBe("Paid");
        bill.GetProperty("items")[0].GetProperty("amount").GetDecimal().ShouldBe(50m);

        // results arrive
        var up = await OkJsonAsync(await Upload(lab.LabId, new[] { (lab.ItemIds[0], 4.5m), (lab.ItemIds[1], 90m) }));
        up.GetProperty("labStatus").GetString().ShouldBe("Completed");
        (await StatusAsync(lab.LabId)).ShouldBe(LabRequestStatus.Completed);

        await using (var db = Db())
        {
            (await db.LabResults.CountAsync(r => r.LabRequestId == lab.LabId)).ShouldBe(1);
            (await db.Notifications.CountAsync(n => n.UserId == lab.Patient.UserId && n.Type == NotificationType.LabResult)).ShouldBe(1);
        }

        var results = await OkJsonAsync(await GetAsync($"/Lab/patient-get-lab-results/{C.Id}?PageNumber=1&PageSize=10", PatientToken(lab.Patient)));
        results.GetProperty("items").GetArrayLength().ShouldBe(1);
        JsonContains(results, "CBC").ShouldBeTrue();

        var requests = await OkJsonAsync(await GetAsync($"/Lab/patient-get-lab-requests/{C.Id}?PageNumber=1&PageSize=10", PatientToken(lab.Patient)));
        requests.GetProperty("items")[0].GetProperty("labStatus").GetString().ShouldBe("Completed");

        // the treating doctor reads the result
        var doctorView = await OkJsonAsync(await GetAsync(
            $"/Lab/doctor-get-patient-lab-result/{C.Id}?patientId={lab.Patient.PatientId}&labRequestId={lab.LabId}", DoctorToken));
        doctorView.GetProperty("labItems").GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public async Task A_receptionist_walk_in_lab_request_goes_through_the_same_steps()
    {
        var cbc = await AddLabPriceAsync("CBC", 30m);
        var p = await C.AddPatientAsync();

        var created = await OkJsonAsync(await PostAsync($"/Lab/receptionist-create-patient-lab-requests/{C.Id}",
            new { patientId = p.PatientId, note = "walk-in", labItems = new[] { new { id = cbc } } }, RecToken));
        var labId = created.GetProperty("labRequestId").GetInt32();
        created.GetProperty("totalPrice").GetDecimal().ShouldBe(30m);
        var items = await ItemIdsAsync(labId);

        await Ok(await Collect(labId));
        await Ok(await Pay(labId));
        await Ok(await Upload(labId, new[] { (items[0], 12.25m) }));

        (await StatusAsync(labId)).ShouldBe(LabRequestStatus.Completed);
        await using var db = Db();
        var pay = await db.Payments.SingleAsync(x => x.LabRequestId == labId);
        pay.Type.ShouldBe(PaymentType.Lab);
        pay.Amount.ShouldBe(30m);
        pay.RecordedByStaffId.ShouldBe(_receptionistId);
        (await db.LabRequestItems.SingleAsync(i => i.Id == items[0])).ResultValue.ShouldBe(12.25m);
    }

    [Fact]
    public async Task An_item_the_lab_could_not_do_is_not_billed_and_cannot_get_a_result()
    {
        var cbc = await AddLabPriceAsync("CBC", 30m);
        var rare = await AddLabPriceAsync("Rare test", 200m);
        var lab = await DoctorRequestsAsync(cbc, rare);

        await Ok(await Collect(lab.LabId, new[] { lab.ItemIds[1] }));
        await Ok(await Pay(lab.LabId));

        await using (var db = Db())
            (await db.Payments.SingleAsync(x => x.LabRequestId == lab.LabId)).Amount.ShouldBe(30m);

        Rejected(await Upload(lab.LabId, new[] { (lab.ItemIds[1], 1m) }));          // the unavailable one
        (await StatusAsync(lab.LabId)).ShouldBe(LabRequestStatus.Processing);

        await Ok(await Upload(lab.LabId, new[] { (lab.ItemIds[0], 7m) }));
        (await StatusAsync(lab.LabId)).ShouldBe(LabRequestStatus.Completed);
    }

    [Fact]
    public async Task Results_uploaded_in_two_rounds_complete_the_request_once()
    {
        var a = await AddLabPriceAsync("A", 10m);
        var b = await AddLabPriceAsync("B", 15m);
        var lab = await DoctorRequestsAsync(a, b);
        await Ok(await Collect(lab.LabId));
        await Ok(await Pay(lab.LabId));

        await Ok(await Upload(lab.LabId, new[] { (lab.ItemIds[0], 1m) }));
        (await StatusAsync(lab.LabId)).ShouldBe(LabRequestStatus.Processing);
        await using (var db = Db())
        {
            (await db.LabResults.CountAsync(r => r.LabRequestId == lab.LabId)).ShouldBe(0);
            (await db.Notifications.CountAsync(n => n.UserId == lab.Patient.UserId && n.Type == NotificationType.LabResult)).ShouldBe(0);
        }
        (await GetAsync($"/Lab/doctor-get-patient-lab-result/{C.Id}?patientId={lab.Patient.PatientId}&labRequestId={lab.LabId}", DoctorToken))
            .StatusCode.ShouldNotBe(HttpStatusCode.OK);                              // nothing to read until it is complete

        await Ok(await Upload(lab.LabId, new[] { (lab.ItemIds[1], 2m) }));
        (await StatusAsync(lab.LabId)).ShouldBe(LabRequestStatus.Completed);
        await using var db2 = Db();
        (await db2.LabResults.CountAsync(r => r.LabRequestId == lab.LabId)).ShouldBe(1);
        (await db2.Notifications.CountAsync(n => n.UserId == lab.Patient.UserId && n.Type == NotificationType.LabResult)).ShouldBe(1);
    }

    [Fact]
    public async Task A_result_file_is_stored_and_a_dangerous_one_is_refused()
    {
        var a = await AddLabPriceAsync("A", 10m);
        var lab = await DoctorRequestsAsync(a);
        await Ok(await Collect(lab.LabId));
        await Ok(await Pay(lab.LabId));

        Rejected(await Upload(lab.LabId, new[] { (lab.ItemIds[0], 1m) }, file: ("virus.exe", "application/pdf")));
        Rejected(await Upload(lab.LabId, new[] { (lab.ItemIds[0], 1m) }, file: ("..\\evil.pdf", "application/pdf")));
        (await StatusAsync(lab.LabId)).ShouldBe(LabRequestStatus.Processing);

        await Ok(await Upload(lab.LabId, new[] { (lab.ItemIds[0], 1m) }, file: ("report.pdf", "application/pdf")));

        await using var db = Db();
        var url = (await db.LabRequestItems.SingleAsync(i => i.Id == lab.ItemIds[0])).ResultFileUrl;
        url.ShouldNotBeNull();
        url!.ShouldStartWith("/uploads/labResult/");
        url.ShouldEndWith(".pdf");
    }

    // ---------- the order of the steps ----------

    [Fact]
    public async Task Steps_cannot_be_skipped_or_repeated()
    {
        var a = await AddLabPriceAsync("A", 10m);
        var lab = await DoctorRequestsAsync(a);

        Rejected(await Pay(lab.LabId));                                              // not collected yet
        Rejected(await Upload(lab.LabId, new[] { (lab.ItemIds[0], 1m) }));           // not paid yet

        await Ok(await Collect(lab.LabId));
        Rejected(await Collect(lab.LabId));                                          // twice
        Rejected(await Upload(lab.LabId, new[] { (lab.ItemIds[0], 1m) }));           // still not paid

        await Ok(await Pay(lab.LabId));
        Rejected(await Pay(lab.LabId));                                              // twice

        await using var db = Db();
        (await db.Payments.CountAsync(p => p.LabRequestId == lab.LabId)).ShouldBe(1);
        (await db.LabRequestItems.SingleAsync(i => i.Id == lab.ItemIds[0])).ResultValue.ShouldBeNull();
    }

    [Fact]
    public async Task A_sample_cannot_be_collected_when_every_item_is_unavailable()
    {
        var a = await AddLabPriceAsync("A", 10m);
        var lab = await DoctorRequestsAsync(a);

        Rejected(await Collect(lab.LabId, lab.ItemIds.ToArray()));

        (await StatusAsync(lab.LabId)).ShouldBe(LabRequestStatus.Pending);
    }

    [Fact]
    public async Task Unknown_ids_are_400_never_500()
    {
        Rejected(await Collect(999999));
        Rejected(await Pay(999999));
        Rejected(await Upload(999999, new[] { (1, 1m) }));
        (await SendAsync(HttpMethod.Get, $"/Lab/technician-get-lab-request/{C.Id}/999999", TechToken)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ---------- cancel ----------

    [Fact]
    public async Task Only_the_one_who_requested_can_cancel_and_only_before_the_sample_is_taken()
    {
        var a = await AddLabPriceAsync("A", 10m);
        var lab = await DoctorRequestsAsync(a);
        var stranger = await C.AddDoctorAsync();
        string Cancel(string _) => $"/Lab/receptionist-or-doctor-cancle-lab-requests/{C.Id}/{lab.LabId}";

        Rejected(await SendAsync(HttpMethod.Put, Cancel(""), Token("Doctor", C.Id, stranger.UserId)));
        Rejected(await SendAsync(HttpMethod.Put, Cancel(""), RecToken));
        Rejected(await SendAsync(HttpMethod.Put, Cancel(""), TechToken));
        (await SendAsync(HttpMethod.Put, Cancel(""), PatientToken(lab.Patient))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await StatusAsync(lab.LabId)).ShouldBe(LabRequestStatus.Pending);

        await Ok(await SendAsync(HttpMethod.Put, Cancel(""), DoctorToken));
        (await StatusAsync(lab.LabId)).ShouldBe(LabRequestStatus.Cancelled);
        Rejected(await Collect(lab.LabId));                                          // a cancelled request cannot be collected

        var pending = await OkJsonAsync(await GetAsync($"/Lab/technician-get-lab-pending-requests/{C.Id}?PageNumber=1&PageSize=50", TechToken));
        pending.GetProperty("items").EnumerateArray().Any(e => e.GetProperty("labRequestId").GetInt32() == lab.LabId).ShouldBeFalse();
    }

    [Fact]
    public async Task Cancelling_after_the_sample_was_collected_is_refused()
    {
        var a = await AddLabPriceAsync("A", 10m);
        var lab = await DoctorRequestsAsync(a);
        await Ok(await Collect(lab.LabId));

        Rejected(await SendAsync(HttpMethod.Put, $"/Lab/receptionist-or-doctor-cancle-lab-requests/{C.Id}/{lab.LabId}", DoctorToken));

        (await StatusAsync(lab.LabId)).ShouldBe(LabRequestStatus.Collected);
    }

    // ---------- editing a pending request ----------

    [Fact]
    public async Task The_doctor_edits_a_pending_request_and_not_after_collection()
    {
        var cbc = await AddLabPriceAsync("CBC", 30m);
        var sugar = await AddLabPriceAsync("Sugar", 20m);
        var lab = await DoctorRequestsAsync(cbc);

        await Ok(await SendAsync(HttpMethod.Put, $"/Lab/doctor-update-lab-request/{C.Id}", DoctorToken,
            new { labRequestId = lab.LabId, patientId = lab.Patient.PatientId, appointmentId = lab.AppointmentId, addLabItems = new[] { new { id = sugar } } }));
        (await ItemIdsAsync(lab.LabId)).Count.ShouldBe(2);

        await Ok(await Collect(lab.LabId));
        Rejected(await SendAsync(HttpMethod.Put, $"/Lab/doctor-update-lab-request/{C.Id}", DoctorToken,
            new { labRequestId = lab.LabId, patientId = lab.Patient.PatientId, appointmentId = lab.AppointmentId, note = "late change" }));

        await using var db = Db();
        (await db.LabRequests.SingleAsync(l => l.Id == lab.LabId)).Notes.ShouldBe("fasting");
    }

    [Fact]
    public async Task The_receptionist_edits_a_walk_in_request_and_the_total_follows()
    {
        var cbc = await AddLabPriceAsync("CBC", 30m);
        var sugar = await AddLabPriceAsync("Sugar", 20m);
        var p = await C.AddPatientAsync();
        var labId = (await OkJsonAsync(await PostAsync($"/Lab/receptionist-create-patient-lab-requests/{C.Id}",
            new { patientId = p.PatientId, labItems = new[] { new { id = cbc }, new { id = sugar } } }, RecToken))).GetProperty("labRequestId").GetInt32();
        var items = await ItemIdsAsync(labId);

        await Ok(await SendAsync(HttpMethod.Put, $"/Lab/receptionist-update-lab-requests/{C.Id}", RecToken,
            new { labRequestId = labId, patientId = p.PatientId, removeLabItems = new[] { items[0] } }));
        await Ok(await Collect(labId));

        var awaiting = await OkJsonAsync(await GetAsync($"/Lab/receptionist-get-lab-requests-await-payment/{C.Id}?PageNumber=1&PageSize=50", RecToken));
        awaiting.GetProperty("items").EnumerateArray().Single(e => e.GetProperty("labRequestId").GetInt32() == labId)
            .GetProperty("totalPrice").GetDecimal().ShouldBe(20m);
    }

    // ---------- bad requests ----------

    [Fact]
    public async Task A_lab_request_must_be_valid()
    {
        var cbc = await AddLabPriceAsync("CBC", 30m);
        var visit = await AddLabPriceAsync("Visit", 50m, ServiceType.Appointment);
        var (p, appt) = await PatientWithVisitAsync();
        var otherDoctor = await C.AddDoctorAsync();
        Task<HttpResponseMessage> Create(object body, string? token = null) =>
            PostAsync($"/Lab/doctor-create-lab-request/{C.Id}", body, token ?? DoctorToken);

        Rejected(await Create(new { patientId = p.PatientId, appointmentId = appt, labItems = Array.Empty<object>() }));            // empty
        Rejected(await Create(new { patientId = p.PatientId, appointmentId = appt, labItems = new[] { new { id = cbc }, new { id = cbc } } })); // duplicate
        Rejected(await Create(new { patientId = p.PatientId, appointmentId = appt, labItems = new[] { new { id = visit } } }));      // not a lab test
        Rejected(await Create(new { patientId = p.PatientId, appointmentId = appt, labItems = new[] { new { id = 999999 } } }));     // unknown
        Rejected(await Create(new { patientId = 999999, appointmentId = appt, labItems = new[] { new { id = cbc } } }));             // unknown patient
        Rejected(await Create(new { patientId = p.PatientId, appointmentId = appt, labItems = new[] { new { id = cbc } } },
            Token("Doctor", C.Id, otherDoctor.UserId)));                                                                           // not their appointment

        await using var db = Db();
        (await db.LabRequests.CountAsync(l => l.PatientId == p.PatientId)).ShouldBe(0);
    }

    // ---------- who may do what ----------

    [Fact]
    public async Task Each_lab_step_belongs_to_one_role()
    {
        var a = await AddLabPriceAsync("A", 10m);
        var lab = await DoctorRequestsAsync(a);
        var pendingUrl = $"/Lab/technician-get-lab-pending-requests/{C.Id}?PageNumber=1&PageSize=10";

        (await GetAsync(pendingUrl)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await GetAsync(pendingUrl, DoctorToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await GetAsync(pendingUrl, RecToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await GetAsync(pendingUrl, PatientToken(lab.Patient))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await Collect(lab.LabId, token: DoctorToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Collect(lab.LabId, token: RecToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        await Ok(await Collect(lab.LabId));

        (await Pay(lab.LabId, TechToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Pay(lab.LabId, DoctorToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Pay(lab.LabId, PatientToken(lab.Patient))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        await Ok(await Pay(lab.LabId));

        (await Upload(lab.LabId, new[] { (lab.ItemIds[0], 1m) }, token: RecToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Upload(lab.LabId, new[] { (lab.ItemIds[0], 1m) }, token: DoctorToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var create = await PostAsync($"/Lab/doctor-create-lab-request/{C.Id}",
            new { patientId = lab.Patient.PatientId, appointmentId = lab.AppointmentId, labItems = new[] { new { id = a } } }, RecToken);
        create.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await StatusAsync(lab.LabId)).ShouldBe(LabRequestStatus.Processing);
    }

    [Fact]
    public async Task Another_patient_never_sees_the_results_or_the_bill()
    {
        var a = await AddLabPriceAsync("Secret-test", 10m);
        var lab = await DoctorRequestsAsync(a);
        await Ok(await Collect(lab.LabId));
        await Ok(await Pay(lab.LabId));
        await Ok(await Upload(lab.LabId, new[] { (lab.ItemIds[0], 1m) }));
        var other = await C.AddPatientAsync();

        foreach (var url in new[] { "patient-get-lab-results", "patient-get-lab-requests" })
        {
            var json = await OkJsonAsync(await GetAsync($"/Lab/{url}/{C.Id}?PageNumber=1&PageSize=10", PatientToken(other)));
            json.GetProperty("items").GetArrayLength().ShouldBe(0);
        }
        var bill = await OkJsonAsync(await GetAsync($"/BillPayment/patient-get-lab-bill/{C.Id}?PageNumber=1&PageSize=10", PatientToken(other)));
        bill.GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task A_parent_sees_a_childs_lab_results_and_a_stranger_does_not()
    {
        var a = await AddLabPriceAsync("A", 10m);
        var parent = await C.AddPatientAsync();
        var child = await C.AddSubPatientAsync(parent);
        var appt = await C.AddAppointmentAsync(Doc.DoctorId, child.PatientId, Date, S0900);
        var labId = (await OkJsonAsync(await PostAsync($"/Lab/doctor-create-lab-request/{C.Id}",
            new { patientId = child.PatientId, appointmentId = appt, labItems = new[] { new { id = a } } }, DoctorToken))).GetProperty("labRequestId").GetInt32();
        var items = await ItemIdsAsync(labId);
        await Ok(await Collect(labId));
        await Ok(await Pay(labId));
        await Ok(await Upload(labId, new[] { (items[0], 3m) }));

        var mine = await OkJsonAsync(await GetAsync(
            $"/Lab/patient-get-lab-results/{C.Id}?PageNumber=1&PageSize=10&subPatientId={child.PatientId}", PatientToken(parent)));
        mine.GetProperty("items").GetArrayLength().ShouldBe(1);

        var stranger = await C.AddPatientAsync();
        Rejected(await GetAsync($"/Lab/patient-get-lab-results/{C.Id}?PageNumber=1&PageSize=10&subPatientId={child.PatientId}", PatientToken(stranger)));
    }
}
