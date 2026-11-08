using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.IntegrationTests.Appointments;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;
using DashSvc = Sehatak.Infrastructure.Services.DashBoardService.AppointmentDashBoard;

namespace Sehatak.IntegrationTests.Dashboard;

[Collection(SqlServerCollection.Name)]
public class AppointmentDashboardTests(SqlServerFixture fx) : AppointmentTestBase(fx)
{
    private DashSvc NewDash() => new(Fx.Harness.NewSharedDb(), Fx.Harness.TenantFactory);

    private async Task AddWaitlistAsync(int patientId, DateOnly date, WaitlistStatus status = WaitlistStatus.Waiting)
    {
        await using var db = Db();
        db.Waitlists.Add(new Waitlist { PatientId = patientId, DoctorId = Doc.DoctorId, PreferredDate = date, Status = status });
        await db.SaveChangesAsync();
    }

    private async Task<string> NameOfUserAsync(int userId)
    {
        await using var db = Db();
        var u = await db.Users.AsNoTracking().SingleAsync(x => x.Id == userId);
        return $"{u.firstName} {u.lastName}";
    }

    // ---------- center summary ----------

    [Fact]
    public async Task The_summary_counts_each_status_for_that_date_only()
    {
        var p2 = await C.AddPatientAsync();
        var p3 = await C.AddPatientAsync();
        var p4 = await C.AddPatientAsync();
        await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900, AppointmentStatus.Confirmed);
        await C.AddAppointmentAsync(Doc.DoctorId, p2.PatientId, Date, S0930, AppointmentStatus.Confirmed);
        await C.AddAppointmentAsync(Doc.DoctorId, p3.PatientId, Date, S1000, AppointmentStatus.Completed);
        await C.AddAppointmentAsync(Doc.DoctorId, p4.PatientId, Date, S1030, AppointmentStatus.Cancelled);
        await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date.AddDays(7), S0900, AppointmentStatus.NoShow);   // another day

        var r = await NewDash().GetCenterAppointmentsSummaryAsync(C.Id, Date);

        r.Date.ShouldBe(Date);
        r.Total.ShouldBe(4);
        r.Confirmed.ShouldBe(2);
        r.Completed.ShouldBe(1);
        r.Cancelled.ShouldBe(1);
        r.NoShow.ShouldBe(0);
    }

    [Fact]
    public async Task Statuses_without_their_own_counter_still_count_in_the_total()
    {
        await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900, AppointmentStatus.Pending);
        await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0930, AppointmentStatus.NoShow);

        var r = await NewDash().GetCenterAppointmentsSummaryAsync(C.Id, Date);

        r.Total.ShouldBe(2);
        r.NoShow.ShouldBe(1);
        (r.Confirmed + r.Completed + r.Cancelled + r.NoShow).ShouldBe(1);
    }

    [Fact]
    public async Task The_waitlist_count_has_only_people_still_waiting_for_that_date()
    {
        var p2 = await C.AddPatientAsync();
        var p3 = await C.AddPatientAsync();
        var p4 = await C.AddPatientAsync();
        await AddWaitlistAsync(Pat.PatientId, Date);
        await AddWaitlistAsync(p2.PatientId, Date);
        await AddWaitlistAsync(p3.PatientId, Date, WaitlistStatus.Entered);
        await AddWaitlistAsync(p4.PatientId, Date.AddDays(7));

        (await NewDash().GetCenterAppointmentsSummaryAsync(C.Id, Date)).WaitlistCount.ShouldBe(2);
    }

    [Fact]
    public async Task An_empty_day_is_all_zeros()
    {
        var r = await NewDash().GetCenterAppointmentsSummaryAsync(C.Id, Date);

        r.Total.ShouldBe(0);
        r.Confirmed.ShouldBe(0);
        r.WaitlistCount.ShouldBe(0);
    }

    [Fact]
    public async Task Without_a_date_the_summary_is_for_today_in_clinic_time()
    {
        await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, ClinicClock.Today, S0900, AppointmentStatus.Confirmed);

        var r = await NewDash().GetCenterAppointmentsSummaryAsync(C.Id);

        r.Date.ShouldBe(ClinicClock.Today);
        r.Total.ShouldBe(1);
    }

    [Fact]
    public async Task The_summary_is_per_center()
    {
        await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900);
        var other = await Fx.Harness.CreateCenterAsync();

        (await NewDash().GetCenterAppointmentsSummaryAsync(other.Id, Date)).Total.ShouldBe(0);
    }

    [Fact]
    public async Task The_summary_fails_for_an_unknown_or_suspended_center()
    {
        await ShouldFailWith("Center.NotFound", () => NewDash().GetCenterAppointmentsSummaryAsync(999_999, Date));

        var suspended = await Fx.Harness.CreateCenterAsync(CenterStatus.Suspended);
        await ShouldFailWith("Center.NotFound", () => NewDash().GetCenterAppointmentsSummaryAsync(suspended.Id, Date));
    }

    // ---------- patient's own list ----------

    [Fact]
    public async Task A_patient_sees_only_their_own_appointments_of_that_date_and_status_in_time_order()
    {
        var other = await C.AddPatientAsync();
        await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S1000, AppointmentStatus.Confirmed);
        await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900, AppointmentStatus.Confirmed);
        await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0930, AppointmentStatus.Cancelled);      // other status
        await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date.AddDays(7), S0900, AppointmentStatus.Confirmed); // other day
        await C.AddAppointmentAsync(Doc.DoctorId, other.PatientId, Date, S1030, AppointmentStatus.Confirmed);    // someone else

        var page = await NewDash().GetPatientAppointmentsAsync(C.Id, Pat.UserId, Date, null, new PagedRequest(), AppointmentStatus.Confirmed);

        page.Items.Select(i => i.TimeSlot).ShouldBe(new TimeOnly?[] { S0900, S1000 });
        page.Items.ShouldAllBe(i => i.PatientId == Pat.PatientId);
    }

    [Fact]
    public async Task The_patient_list_shows_the_patient_and_doctor_names()
    {
        await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900);

        var item = (await NewDash().GetPatientAppointmentsAsync(C.Id, Pat.UserId, Date, null, new PagedRequest(), AppointmentStatus.Confirmed))
            .Items.ShouldHaveSingleItem();

        item.PatientName.ShouldBe(await NameOfUserAsync(Pat.UserId));
        item.DoctorName.ShouldBe(await NameOfUserAsync(Doc.UserId));
        item.DoctorId.ShouldBe(Doc.DoctorId);
    }

    [Fact]
    public async Task A_parent_can_list_a_childs_appointments_under_the_childs_name()
    {
        var kid = await C.AddSubPatientAsync(Pat);
        await C.AddAppointmentAsync(Doc.DoctorId, kid.PatientId, Date, S0900);
        await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0930);   // the parent's own must not show up

        var item = (await NewDash().GetPatientAppointmentsAsync(C.Id, Pat.UserId, Date, kid.PatientId, new PagedRequest(), AppointmentStatus.Confirmed))
            .Items.ShouldHaveSingleItem();

        item.PatientId.ShouldBe(kid.PatientId);
        item.PatientName.ShouldBe("P Test");
    }

    [Fact]
    public async Task A_child_of_someone_else_cannot_be_listed()
    {
        var other = await C.AddPatientAsync();
        var theirKid = await C.AddSubPatientAsync(other);
        await C.AddAppointmentAsync(Doc.DoctorId, theirKid.PatientId, Date, S0900);

        await ShouldFailWith("SubPatient.NotFound",
            () => NewDash().GetPatientAppointmentsAsync(C.Id, Pat.UserId, Date, theirKid.PatientId, new PagedRequest(), AppointmentStatus.Confirmed));
        await ShouldFailWith("SubPatient.NotFound",
            () => NewDash().GetPatientAppointmentsAsync(C.Id, Pat.UserId, Date, other.PatientId, new PagedRequest(), AppointmentStatus.Confirmed));
    }

    [Fact]
    public async Task The_patient_list_is_paged()
    {
        await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900);
        await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0930);
        await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S1000);

        var p1 = await NewDash().GetPatientAppointmentsAsync(C.Id, Pat.UserId, Date, null, new PagedRequest { PageNumber = 1, PageSize = 2 }, AppointmentStatus.Confirmed);
        var p2 = await NewDash().GetPatientAppointmentsAsync(C.Id, Pat.UserId, Date, null, new PagedRequest { PageNumber = 2, PageSize = 2 }, AppointmentStatus.Confirmed);

        p1.Items.Count.ShouldBe(2);
        p2.Items.Count.ShouldBe(1);
        p1.TotalCount.ShouldBe(3);
    }

    [Fact]
    public async Task The_patient_list_needs_an_active_patient_in_an_available_center()
    {
        var gone = await C.AddPatientAsync(active: false);

        await ShouldFailWith("Patient.NotFound", () => NewDash().GetPatientAppointmentsAsync(C.Id, Doc.UserId, Date, null, new PagedRequest(), AppointmentStatus.Confirmed));
        await ShouldFailWith("Patient.NotFound", () => NewDash().GetPatientAppointmentsAsync(C.Id, gone.UserId, Date, null, new PagedRequest(), AppointmentStatus.Confirmed));
        await ShouldFailWith("Center.NotFound", () => NewDash().GetPatientAppointmentsAsync(999_999, Pat.UserId, Date, null, new PagedRequest(), AppointmentStatus.Confirmed));
    }

    // ---------- receptionist's list ----------

    [Fact]
    public async Task The_receptionist_sees_confirmed_and_completed_of_the_day_with_names_and_bill()
    {
        var rec = await C.AddReceptionistAsync();
        var kid = await C.AddSubPatientAsync(Pat);
        var confirmed = await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900, AppointmentStatus.Confirmed);
        await C.AddAppointmentAsync(Doc.DoctorId, kid.PatientId, Date, S0930, AppointmentStatus.Completed);
        await using (var db = Db())
        {
            var a = await db.Appointments.SingleAsync(x => x.Id == confirmed);
            a.BillAmount = 75m;
            await db.SaveChangesAsync();
        }

        var page = await NewDash().GetReceptionistAppointmentsAsync(C.Id, rec, Date, new PagedRequest());

        page.Items.Count.ShouldBe(2);
        var first = page.Items[0];
        first.AppointmentId.ShouldBe(confirmed);
        first.PatientName.ShouldBe(await NameOfUserAsync(Pat.UserId));
        first.DoctorName.ShouldBe(await NameOfUserAsync(Doc.UserId));
        first.BillAmount.ShouldBe(75m);
        first.PaymentStatus.ShouldBeNull();      // no payment row yet
        page.Items[1].PatientName.ShouldBe("P Test");
    }

    [Fact]
    public async Task The_receptionist_sees_every_status_of_the_day_and_each_row_says_which_one()
    {
        var rec = await C.AddReceptionistAsync();
        await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900, AppointmentStatus.Confirmed);
        await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0930, AppointmentStatus.Completed);
        await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S1000, AppointmentStatus.Cancelled);
        await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S1030, AppointmentStatus.NoShow);

        var page = await NewDash().GetReceptionistAppointmentsAsync(C.Id, rec, Date, new PagedRequest());

        page.Items.Select(i => i.AppointmentStatus).ShouldBe(new[] { "Confirmed", "Completed", "Cancelled", "NoShow" });
    }

    [Fact]
    public async Task Each_patient_row_carries_its_status()
    {
        await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900, AppointmentStatus.Cancelled);

        var item = (await NewDash().GetPatientAppointmentsAsync(C.Id, Pat.UserId, Date, null, new PagedRequest(), AppointmentStatus.Cancelled))
            .Items.ShouldHaveSingleItem();

        item.AppointmentStatus.ShouldBe("Cancelled");
    }

    [Fact]
    public async Task The_receptionist_list_has_only_the_asked_date()
    {
        // the old filter was (date = d AND Confirmed) OR Completed: every Completed appointment of every day showed up
        var rec = await C.AddReceptionistAsync();
        await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900, AppointmentStatus.Completed);
        await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date.AddDays(-30), S0900, AppointmentStatus.Completed);
        await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date.AddDays(7), S0900, AppointmentStatus.Completed);

        var page = await NewDash().GetReceptionistAppointmentsAsync(C.Id, rec, Date, new PagedRequest());

        page.Items.ShouldHaveSingleItem();
        page.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task The_receptionist_list_is_in_time_order_and_paged()
    {
        var rec = await C.AddReceptionistAsync();
        await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S1000);
        await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900);
        await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0930);

        var p1 = await NewDash().GetReceptionistAppointmentsAsync(C.Id, rec, Date, new PagedRequest { PageNumber = 1, PageSize = 2 });
        var p2 = await NewDash().GetReceptionistAppointmentsAsync(C.Id, rec, Date, new PagedRequest { PageNumber = 2, PageSize = 2 });

        p1.Items.Select(i => i.TimeSlot).ShouldBe(new TimeOnly?[] { S0900, S0930 });
        p2.Items.Select(i => i.TimeSlot).ShouldBe(new TimeOnly?[] { S1000 });
    }

    [Fact]
    public async Task The_receptionist_list_needs_an_active_user_in_an_available_center()
    {
        var gone = await C.AddReceptionistAsync(active: false);

        await ShouldFailWith("User.NotFound", () => NewDash().GetReceptionistAppointmentsAsync(C.Id, gone, Date, new PagedRequest()));
        await ShouldFailWith("User.NotFound", () => NewDash().GetReceptionistAppointmentsAsync(C.Id, 999_999, Date, new PagedRequest()));
        await ShouldFailWith("Center.NotFound", () => NewDash().GetReceptionistAppointmentsAsync(999_999, gone, Date, new PagedRequest()));
    }
}
