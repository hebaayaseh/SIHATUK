using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Appointments;

/// <summary>GetPatientsWaitListsAsync (staff view of a day) and GetPatientWaitListsAsync (one patient's entry).</summary>
[Collection(SqlServerCollection.Name)]
public class WaitListViewTests(SqlServerFixture fx) : AppointmentTestBase(fx)
{
    private static readonly PagedRequest Page = new() { PageNumber = 1, PageSize = 10 };

    private async Task<int> AddWait(int patientId, DateOnly date, WaitlistStatus status = WaitlistStatus.Waiting, int? doctorId = null)
    {
        await using var db = Db();
        var w = new Waitlist
        {
            PatientId = patientId, DoctorId = doctorId ?? Doc.DoctorId, PreferredDate = date,
            Status = status, CreatedAt = DateTime.UtcNow
        };
        db.Waitlists.Add(w);
        await db.SaveChangesAsync();
        return w.Id;
    }

    // ------------------------------------------------------------------ staff list for a day

    [Fact]
    public async Task Staff_list_shows_only_that_doctor_and_that_day()
    {
        var other = await C.AddPatientAsync();
        var doc2 = await C.AddDoctorAsync();
        await AddWait(Pat.PatientId, Date);
        await AddWait(other.PatientId, Date.AddDays(7));                      // other day
        await AddWait(other.PatientId, Date, doctorId: doc2.DoctorId);        // other doctor

        var res = await C.NewAppointmentService().GetPatientsWaitListsAsync(C.Id, Doc.DoctorId, Date, Page);

        res.TotalCount.ShouldBe(1);
        res.Items.Single().PatientId.ShouldBe(Pat.PatientId);
    }

    [Fact]
    public async Task Staff_list_shows_name_phone_and_email_and_for_a_child_uses_the_parents_contact()
    {
        var sub = await C.AddSubPatientAsync(Pat);
        await AddWait(Pat.PatientId, Date);
        await AddWait(sub.PatientId, Date, WaitlistStatus.Entered);

        var res = await C.NewAppointmentService().GetPatientsWaitListsAsync(C.Id, Doc.DoctorId, Date, Page);

        await using var db = Db();
        var parent = await db.Users.SingleAsync(u => u.Id == Pat.UserId);
        var adult = res.Items.Single(i => i.PatientId == Pat.PatientId);
        adult.PatientName.ShouldBe($"{parent.firstName} {parent.lastName}");
        adult.PhoneNumber.ShouldBe(parent.phoneNumber);
        adult.Email.ShouldBe(parent.email);
        adult.status.ShouldBe(WaitlistStatus.Waiting);

        var child = res.Items.Single(i => i.PatientId == sub.PatientId);
        child.PatientName.ShouldBe("P Test");                  // the child's own first/last name
        child.PhoneNumber.ShouldBe(parent.phoneNumber);        // parent's contact details
        child.Email.ShouldBe(parent.email);
        child.status.ShouldBe(WaitlistStatus.Entered);
        child.date.ShouldBe(Date);
    }

    [Fact]
    public async Task Staff_list_is_paged()
    {
        for (var i = 0; i < 3; i++)
            await AddWait((await C.AddPatientAsync()).PatientId, Date);

        var res = await C.NewAppointmentService().GetPatientsWaitListsAsync(C.Id, Doc.DoctorId, Date,
            new PagedRequest { PageNumber = 1, PageSize = 2 });

        res.TotalCount.ShouldBe(3);
        res.Items.Count.ShouldBe(2);
        res.TotalPages.ShouldBe(2);
    }

    [Fact]
    public async Task Staff_list_is_empty_when_nobody_waits()
    {
        var res = await C.NewAppointmentService().GetPatientsWaitListsAsync(C.Id, Doc.DoctorId, Date, Page);

        res.TotalCount.ShouldBe(0);
        res.Items.ShouldBeEmpty();
    }

    [Fact]
    public Task Staff_list_Doctor_NotFound() =>
        ShouldFailWith("Doctor.NotFound", () => C.NewAppointmentService().GetPatientsWaitListsAsync(C.Id, 999_999, Date, Page));

    [Fact]
    public Task Staff_list_Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => C.NewAppointmentService().GetPatientsWaitListsAsync(999_999, Doc.DoctorId, Date, Page));

    // ------------------------------------------------------------------ one patient's entry

    [Fact]
    public async Task Patient_sees_their_own_entry()
    {
        var id = await AddWait(Pat.PatientId, Date);

        var res = await C.NewAppointmentService().GetPatientWaitListsAsync(C.Id, Doc.DoctorId, Pat.UserId, Date, null);

        res.WaitLisId.ShouldBe(id);
        res.PatientId.ShouldBe(Pat.PatientId);
        res.status.ShouldBe(WaitlistStatus.Waiting);
    }

    [Fact]
    public async Task Parent_sees_the_childs_entry()
    {
        var sub = await C.AddSubPatientAsync(Pat);
        await AddWait(sub.PatientId, Date);

        var res = await C.NewAppointmentService().GetPatientWaitListsAsync(C.Id, Doc.DoctorId, Pat.UserId, Date, sub.PatientId);

        res.PatientId.ShouldBe(sub.PatientId);
    }

    [Fact]
    public Task WaitList_NotFound_when_the_patient_is_not_waiting() =>
        ShouldFailWith("WaitList.NotFound", () => C.NewAppointmentService().GetPatientWaitListsAsync(C.Id, Doc.DoctorId, Pat.UserId, Date, null));

    [Fact]
    public async Task WaitList_NotFound_for_a_different_day()
    {
        await AddWait(Pat.PatientId, Date.AddDays(7));
        await ShouldFailWith("WaitList.NotFound", () => C.NewAppointmentService().GetPatientWaitListsAsync(C.Id, Doc.DoctorId, Pat.UserId, Date, null));
    }

    [Fact]
    public async Task SubPatient_NotFoundOrNotOwned_for_someone_elses_child()
    {
        var stranger = await C.AddPatientAsync();
        var child = await C.AddSubPatientAsync(stranger);

        await ShouldFailWith("SubPatient.NotFoundOrNotOwned",
            () => C.NewAppointmentService().GetPatientWaitListsAsync(C.Id, Doc.DoctorId, Pat.UserId, Date, child.PatientId));
    }

    [Fact]
    public Task Patient_NotFound() =>
        ShouldFailWith("Patient.NotFound", () => C.NewAppointmentService().GetPatientWaitListsAsync(C.Id, Doc.DoctorId, 999_999, Date, null));

    [Fact]
    public Task Doctor_NotFound() =>
        ShouldFailWith("Doctor.NotFound", () => C.NewAppointmentService().GetPatientWaitListsAsync(C.Id, 999_999, Pat.UserId, Date, null));

    [Fact]
    public Task Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => C.NewAppointmentService().GetPatientWaitListsAsync(999_999, Doc.DoctorId, Pat.UserId, Date, null));

    /// <summary>
    /// The staff list fills `date`, but the single-patient query forgets `date = n.PreferredDate` in its Select,
    /// so the response carries 0001-01-01. Fix: add `date = n.PreferredDate` to that projection.
    /// </summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Patient_entry_carries_the_waitlist_date()
    {
        await AddWait(Pat.PatientId, Date);

        var res = await C.NewAppointmentService().GetPatientWaitListsAsync(C.Id, Doc.DoctorId, Pat.UserId, Date, null);

        res.date.ShouldBe(Date);
    }
}
