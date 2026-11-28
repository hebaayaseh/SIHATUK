using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.PaymentEnums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Api;

/// <summary>
/// E2E batch 8c: the three appointment dashboards and the money reports. The numbers come from real visits
/// (check-in -> doctor -> bill -> payment over HTTP) plus rows seeded for the other statuses.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class DashboardsAndFinanceFlowTests(SqlServerFixture fx) : FlowTestBase(fx)
{
    private static DateOnly Today => ClinicClock.Today;
    private int _receptionistId;
    private string RecToken => ReceptionistToken(_receptionistId);
    private string PatientToken(PatientInfo p) => Token("Patient", C.Id, p.UserId);

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _receptionistId = await C.AddReceptionistAsync();
        await AddVisitPriceAsync();                       // the visit costs 50
    }

    private static async Task Ok(HttpResponseMessage r) => r.StatusCode.ShouldBe(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());

    private async Task<(PatientInfo P, int Appt)> SeedAsync(TimeOnly slot, AppointmentStatus s, DateOnly? date = null)
    {
        var p = await C.AddPatientAsync();
        return (p, await C.AddAppointmentAsync(Doc.DoctorId, p.PatientId, date ?? Date, slot, s));
    }

    /// <summary>A whole paid visit today over HTTP: check-in, next, record, finish, pay.</summary>
    private async Task<int> PaidVisitTodayAsync(TimeOnly slot)
    {
        var (p, appt) = await SeedAsync(slot, AppointmentStatus.Confirmed, Today);
        await Ok(await SendAsync(HttpMethod.Put, $"/CheckTime/receptionist-checkIn-patient/{C.Id}/{appt}", RecToken));
        await Ok(await SendAsync(HttpMethod.Put, $"/CheckTime/receptionist-next-patient/{C.Id}/{appt}", RecToken));
        await Ok(await PostAsync($"/MedicalRecord/doctor-add-medicalrecord/{C.Id}",
            new { patientId = p.PatientId, appointmentId = appt, diagnosis = "flu", prescription = "rest", notes = "n", items = (object?)null }, DoctorToken));
        await Ok(await SendAsync(HttpMethod.Put, $"/CheckTime/doctor-finish-appointment-time/{C.Id}/{appt}", DoctorToken));
        await Ok(await SendAsync(HttpMethod.Post, $"/ConfirmPayment/receptionist-appointment-paid/{C.Id}/{appt}", RecToken, new { method = 1 }));
        return appt;
    }

    // ---------- admin summary ----------

    [Fact]
    public async Task The_admin_summary_counts_the_day_by_status_and_the_waiting_list()
    {
        await SeedAsync(new TimeOnly(9, 0), AppointmentStatus.Confirmed);
        await SeedAsync(new TimeOnly(9, 30), AppointmentStatus.Confirmed);
        await SeedAsync(new TimeOnly(10, 0), AppointmentStatus.Completed);
        await SeedAsync(new TimeOnly(10, 30), AppointmentStatus.Cancelled);
        await SeedAsync(new TimeOnly(11, 0), AppointmentStatus.NoShow);
        await SeedAsync(new TimeOnly(9, 0), AppointmentStatus.Confirmed, Date.AddDays(7));       // another day: must not count

        var waiter = await C.AddPatientAsync();
        await using (var db = Db())
        {
            db.Waitlists.Add(new Waitlist { PatientId = waiter.PatientId, DoctorId = Doc.DoctorId, PreferredDate = Date, Status = WaitlistStatus.Waiting, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var s = await OkJsonAsync(await GetAsync($"/AppointmentDashboard/appointments-summary/{C.Id}?date={DateStr}", AdminToken));

        s.GetProperty("total").GetInt32().ShouldBe(5);
        s.GetProperty("confirmed").GetInt32().ShouldBe(2);
        s.GetProperty("completed").GetInt32().ShouldBe(1);
        s.GetProperty("cancelled").GetInt32().ShouldBe(1);
        s.GetProperty("noShow").GetInt32().ShouldBe(1);
        s.GetProperty("waitlistCount").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task The_admin_summary_without_a_date_is_about_today()
    {
        await SeedAsync(new TimeOnly(9, 0), AppointmentStatus.Confirmed, Today);
        await SeedAsync(new TimeOnly(9, 30), AppointmentStatus.Confirmed);   // the future Monday

        var s = await OkJsonAsync(await GetAsync($"/AppointmentDashboard/appointments-summary/{C.Id}", AdminToken));

        s.GetProperty("total").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task The_summary_follows_the_real_visit_through_the_day()
    {
        await PaidVisitTodayAsync(new TimeOnly(9, 0));

        var s = await OkJsonAsync(await GetAsync($"/AppointmentDashboard/appointments-summary/{C.Id}?date={Today:yyyy-MM-dd}", AdminToken));

        s.GetProperty("completed").GetInt32().ShouldBe(1);
        s.GetProperty("confirmed").GetInt32().ShouldBe(0);
    }

    // ---------- receptionist dashboard ----------

    [Fact]
    public async Task The_receptionist_dashboard_lists_the_day_with_the_payment_state()
    {
        var paid = await PaidVisitTodayAsync(new TimeOnly(9, 0));
        var (_, waiting) = await SeedAsync(new TimeOnly(9, 30), AppointmentStatus.Confirmed, Today);
        await SeedAsync(new TimeOnly(9, 0), AppointmentStatus.Confirmed);         // another day

        var d = await OkJsonAsync(await GetAsync($"/AppointmentDashboard/receptionist-appointment-dashboard/{C.Id}?date={Today:yyyy-MM-dd}&PageNumber=1&PageSize=10", RecToken));

        d.GetProperty("items").GetArrayLength().ShouldBe(2);
        JsonContains(d, "Paid").ShouldBeTrue(d.ToString());
        JsonContains(d, "Completed").ShouldBeTrue(d.ToString());
        JsonContains(d, "Confirmed").ShouldBeTrue(d.ToString());

        // the other day shows only its own appointment
        var other = await OkJsonAsync(await GetAsync($"/AppointmentDashboard/receptionist-appointment-dashboard/{C.Id}?date={DateStr}&PageNumber=1&PageSize=10", RecToken));
        other.GetProperty("items").GetArrayLength().ShouldBe(1);
        paid.ShouldBeGreaterThan(0);
        waiting.ShouldBeGreaterThan(0);
    }

    // ---------- patient dashboard ----------

    [Fact]
    public async Task The_patient_dashboard_shows_only_his_own_appointments_filtered_by_status()
    {
        var mine = await SeedAsync(new TimeOnly(9, 0), AppointmentStatus.Confirmed);
        await SeedAsync(new TimeOnly(9, 30), AppointmentStatus.Confirmed);                     // somebody else's
        var cancelled = await C.AddAppointmentAsync(Doc.DoctorId, mine.P.PatientId, Date, new TimeOnly(10, 0), AppointmentStatus.Cancelled);

        string Url(string status, int? sub = null) =>
            $"/AppointmentDashboard/patient-appointment-dashboard/{C.Id}?date={DateStr}&PageNumber=1&PageSize=10&status={status}{(sub != null ? $"&subPatientId={sub}" : "")}";

        var confirmed = await OkJsonAsync(await GetAsync(Url("Confirmed"), PatientToken(mine.P)));
        confirmed.GetProperty("items").GetArrayLength().ShouldBe(1);

        var cancels = await OkJsonAsync(await GetAsync(Url("Cancelled"), PatientToken(mine.P)));
        cancels.GetProperty("items").GetArrayLength().ShouldBe(1);

        var none = await OkJsonAsync(await GetAsync(Url("Completed"), PatientToken(mine.P)));
        none.GetProperty("items").GetArrayLength().ShouldBe(0);
        cancelled.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task A_parent_sees_his_sub_patients_appointments_but_not_a_strangers()
    {
        var parent = await C.AddPatientAsync();
        var sub = await C.AddSubPatientAsync(parent);
        await C.AddAppointmentAsync(Doc.DoctorId, sub.PatientId, Date, new TimeOnly(9, 0), AppointmentStatus.Confirmed);
        var stranger = await C.AddSubPatientAsync(await C.AddPatientAsync());

        string Url(int s) => $"/AppointmentDashboard/patient-appointment-dashboard/{C.Id}?date={DateStr}&PageNumber=1&PageSize=10&status=Confirmed&subPatientId={s}";

        (await OkJsonAsync(await GetAsync(Url(sub.PatientId), PatientToken(parent)))).GetProperty("items").GetArrayLength().ShouldBe(1);
        ((int)(await GetAsync(Url(stranger.PatientId), PatientToken(parent))).StatusCode).ShouldBe(400);
    }

    // ---------- money ----------

    [Fact]
    public async Task The_monthly_report_adds_up_real_payments_and_expenses()
    {
        await PaidVisitTodayAsync(new TimeOnly(9, 0));
        await PaidVisitTodayAsync(new TimeOnly(9, 30));

        // a finished visit that was never paid and a cancelled one must not count as income
        await SeedAsync(new TimeOnly(10, 0), AppointmentStatus.Completed, Today);
        await SeedAsync(new TimeOnly(10, 30), AppointmentStatus.Cancelled, Today);

        await using (var db = Db())
        {
            db.Expenses.Add(new Expense { Name = "rent", Amount = 30m, Section = ExpenseSection.Center, ExpenseDate = Today, CreatedByUserId = C.AdminUserId });
            await db.SaveChangesAsync();
        }

        var r = await OkJsonAsync(await GetAsync($"/FinancialReport/admin-net-profit/{C.Id}?year={Today.Year}&month={Today.Month}&includeDays=true", AdminToken));

        r.GetProperty("specialty").GetProperty("receipts").GetDecimal().ShouldBe(100m);
        r.GetProperty("monthTotal").GetDecimal().ShouldBe(100m);
        r.GetProperty("centerExpenses").GetDecimal().ShouldBe(30m);
        var salaries = r.GetProperty("salaries").GetProperty("totalSalaries").GetDecimal();
        r.GetProperty("netProfit").GetDecimal().ShouldBe(100m - 30m - salaries);

        // with includeDays the money sits on today's row
        var days = r.GetProperty("days").EnumerateArray().ToList();
        days.Count.ShouldBe(DateTime.DaysInMonth(Today.Year, Today.Month));
        days.Single(x => x.GetProperty("date").GetString() == Today.ToString("yyyy-MM-dd")).GetProperty("specialty").GetProperty("receipts").GetDecimal().ShouldBe(100m);

        // without it the totals stay and the days are gone
        var short_ = await OkJsonAsync(await GetAsync($"/FinancialReport/admin-net-profit/{C.Id}?year={Today.Year}&month={Today.Month}", AdminToken));
        short_.GetProperty("days").GetArrayLength().ShouldBe(0);
        short_.GetProperty("monthTotal").GetDecimal().ShouldBe(100m);
    }

    [Fact]
    public async Task A_month_with_no_activity_is_all_zeros()
    {
        await PaidVisitTodayAsync(new TimeOnly(9, 0));
        var other = Today.AddMonths(-2);

        var r = await OkJsonAsync(await GetAsync($"/FinancialReport/admin-net-profit/{C.Id}?year={other.Year}&month={other.Month}", AdminToken));

        r.GetProperty("monthTotal").GetDecimal().ShouldBe(0m);
        r.GetProperty("specialty").GetProperty("receipts").GetDecimal().ShouldBe(0m);
    }

    [Fact]
    public async Task The_excel_report_downloads_as_a_real_spreadsheet()
    {
        await PaidVisitTodayAsync(new TimeOnly(9, 0));

        var res = await GetAsync($"/FinancialReport/admin/{C.Id}?year={Today.Year}&month={Today.Month}", AdminToken);

        res.StatusCode.ShouldBe(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        res.Content.Headers.ContentType!.MediaType.ShouldBe("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        var bytes = await res.Content.ReadAsByteArrayAsync();
        bytes.Length.ShouldBeGreaterThan(1000);
        (bytes[0], bytes[1]).ShouldBe(((byte)'P', (byte)'K'));          // an .xlsx is a zip file
    }

    [Theory]
    [InlineData(2026, 0)]
    [InlineData(2026, 13)]
    [InlineData(0, 5)]
    [InlineData(10000, 5)]
    public async Task A_bad_year_or_month_is_a_clean_400(int year, int month)
    {
        var res = await GetAsync($"/FinancialReport/admin-net-profit/{C.Id}?year={year}&month={month}", AdminToken);

        ((int)res.StatusCode).ShouldBe(400);
    }

    [Fact]
    public async Task Only_the_owner_sees_money_and_only_staff_see_dashboards()
    {
        foreach (var role in new[] { "Doctor", "Receptionist", "Patient", "LabTechnician" })
        {
            (await GetAsync($"/FinancialReport/admin-net-profit/{C.Id}?year={Today.Year}&month={Today.Month}", Token(role, C.Id, 1))).StatusCode.ShouldBe(HttpStatusCode.Forbidden, role);
            (await GetAsync($"/FinancialReport/admin/{C.Id}?year={Today.Year}", Token(role, C.Id, 1))).StatusCode.ShouldBe(HttpStatusCode.Forbidden, role);
            (await GetAsync($"/AppointmentDashboard/appointments-summary/{C.Id}", Token(role, C.Id, 1))).StatusCode.ShouldBe(HttpStatusCode.Forbidden, role);
        }

        (await GetAsync($"/AppointmentDashboard/receptionist-appointment-dashboard/{C.Id}?date={DateStr}&PageNumber=1&PageSize=10", AdminToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await GetAsync($"/AppointmentDashboard/patient-appointment-dashboard/{C.Id}?date={DateStr}&PageNumber=1&PageSize=10&status=Confirmed", RecToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await GetAsync($"/FinancialReport/admin-net-profit/{C.Id}?year={Today.Year}&month={Today.Month}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await GetAsync($"/FinancialReport/admin-net-profit/{C.Id + 1}?year={Today.Year}&month={Today.Month}", AdminToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
