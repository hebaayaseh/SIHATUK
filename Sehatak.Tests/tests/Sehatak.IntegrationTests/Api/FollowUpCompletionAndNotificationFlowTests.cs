using System.Net;
using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Domain.Entities;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Api;

/// <summary>
/// E2E batch 5 (decisions): a booked follow-up becomes Completed when its visit completes,
/// and the doctor is told when the patient cancels a consultation request.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class FollowUpCompletionAndNotificationFlowTests(SqlServerFixture fx) : FlowTestBase(fx)
{
    private static DateOnly Today => ClinicClock.Today;
    private int _receptionistId;
    private string RecToken => ReceptionistToken(_receptionistId);

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _receptionistId = await C.AddReceptionistAsync();
        await AddVisitPriceAsync();
        await using var db = Db();
        db.ServicePrices.Add(new ServicePrice { ServiceName = "Follow-up", Price = 20m, Type = ServiceType.FollowUp });
        await db.Doctors.Where(d => d.Id == Doc.DoctorId).ExecuteUpdateAsync(s => s.SetProperty(d => d.OnlineEnabled, true));
        await db.SaveChangesAsync();
    }

    private static async Task Ok(HttpResponseMessage r) => r.StatusCode.ShouldBe(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());

    /// <summary>A follow-up that is already booked for a visit today (booking itself is covered elsewhere).</summary>
    private async Task<(PatientInfo P, int Appt, int FollowUpId)> BookedFollowUpTodayAsync()
    {
        var p = await C.AddPatientAsync();
        var followUpId = await C.AddFollowUpAsync(Doc.DoctorId, p.PatientId, Today.AddDays(10));
        var appt = await C.AddAppointmentAsync(Doc.DoctorId, p.PatientId, Today, S0900);
        await using var db = Db();
        await db.Appointments.Where(a => a.Id == appt).ExecuteUpdateAsync(s => s.SetProperty(a => a.IsFollowUp, true));
        await db.FollowUps.Where(f => f.Id == followUpId).ExecuteUpdateAsync(s => s
            .SetProperty(f => f.Status, FollowUpStatus.Booked)
            .SetProperty(f => f.ScheduledAppointmentId, (int?)appt));
        return (p, appt, followUpId);
    }

    private async Task<FollowUpStatus> StatusAsync(int followUpId)
    {
        await using var db = Db();
        return await db.FollowUps.Where(f => f.Id == followUpId).Select(f => f.Status).SingleAsync();
    }

    [Fact]
    public async Task The_follow_up_completes_when_the_doctor_finishes_its_visit()
    {
        var (_, appt, followUpId) = await BookedFollowUpTodayAsync();

        await Ok(await SendAsync(HttpMethod.Put, $"/CheckTime/receptionist-next-patient/{C.Id}/{appt}", RecToken));
        (await StatusAsync(followUpId)).ShouldBe(FollowUpStatus.Booked);            // not yet: the visit is only in progress

        await Ok(await SendAsync(HttpMethod.Put, $"/CheckTime/doctor-finish-appointment-time/{C.Id}/{appt}", DoctorToken));

        (await StatusAsync(followUpId)).ShouldBe(FollowUpStatus.Completed);
    }

    [Fact]
    public async Task The_follow_up_completes_when_the_receptionist_closes_the_visit_with_payment()
    {
        var (p, appt, followUpId) = await BookedFollowUpTodayAsync();
        await Ok(await SendAsync(HttpMethod.Put, $"/CheckTime/receptionist-next-patient/{C.Id}/{appt}", RecToken));
        await Ok(await PostAsync($"/MedicalRecord/doctor-add-medicalrecord/{C.Id}",
            new { patientId = p.PatientId, appointmentId = appt, diagnosis = "ok", prescription = "r", notes = "n" }, DoctorToken));

        await Ok(await SendAsync(HttpMethod.Post, $"/ConfirmPayment/receptionist-appointment-paid/{C.Id}/{appt}", RecToken, new { method = 1 }));

        (await StatusAsync(followUpId)).ShouldBe(FollowUpStatus.Completed);
    }

    [Fact]
    public async Task A_follow_up_stays_booked_when_a_normal_visit_of_the_same_patient_finishes()
    {
        var (p, _, followUpId) = await BookedFollowUpTodayAsync();
        var other = await C.AddAppointmentAsync(Doc.DoctorId, p.PatientId, Today, S0930);

        await Ok(await SendAsync(HttpMethod.Put, $"/CheckTime/receptionist-next-patient/{C.Id}/{other}", RecToken));
        await Ok(await SendAsync(HttpMethod.Put, $"/CheckTime/doctor-finish-appointment-time/{C.Id}/{other}", DoctorToken));

        (await StatusAsync(followUpId)).ShouldBe(FollowUpStatus.Booked);
    }

    [Fact]
    public async Task The_doctor_is_told_when_the_patient_cancels_a_consultation_request()
    {
        var p = await C.AddPatientAsync();
        var token = Token("Patient", C.Id, p.UserId);
        await Ok(await SendAsync(HttpMethod.Post, $"/Consultation/patient-request/{C.Id}/{Doc.DoctorId}", token));
        int id;
        await using (var db0 = Db())
            id = await db0.Consultations.Where(c => c.PatientId == p.PatientId).Select(c => c.Id).SingleAsync();

        await Ok(await SendAsync(HttpMethod.Put, $"/Consultation/patient-cancel/{C.Id}/{id}", token));

        await using var db = Db();
        (await db.Notifications.AnyAsync(n => n.UserId == Doc.UserId && n.Type == NotificationType.Cancellation)).ShouldBeTrue();
    }
}
