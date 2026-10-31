using Sehatak.Application.Common;
using Sehatak.Domain.Enums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.FollowUps;

/// <summary>The three list screens: doctor (his own), receptionist (Pending, grouped by doctor), patient (own / child).</summary>
[Collection(SqlServerCollection.Name)]
public class FollowUpQueryTests(SqlServerFixture fx) : FollowUpTestBase(fx)
{
    private static PagedRequest Page(int n = 1, int size = 10) => new() { PageNumber = n, PageSize = size };

    // ------------------------------------------------------------------ Doctor

    [Fact]
    public async Task Doctor_sees_only_his_own_follow_ups_newest_first()
    {
        var doc2 = await C.AddDoctorAsync();
        var older = await SeedFollowUpAsync(createdUtc: DateTime.UtcNow.AddHours(-5));
        var newer = await SeedFollowUpAsync(status: FollowUpStatus.Booked, createdUtc: DateTime.UtcNow.AddHours(-1));
        await SeedFollowUpAsync(doctorId: doc2.DoctorId);

        var res = await NewFollowService().DoctorGetAllFollowUpAsync(C.Id, Doc.UserId, Page());

        res.TotalCount.ShouldBe(2);
        res.Items.Select(i => i.FollowUpId).ToList().ShouldBe([newer, older]);
        res.Items[0].followUpStatus.ShouldBe(FollowUpStatus.Booked);
        res.Items[0].PatientId.ShouldBe(Pat.PatientId);
        res.Items[0].PatientName.ShouldNotBeNullOrWhiteSpace();
        res.Items[0].AllowFollowUpDate.ShouldBe(FollowUpDate);
    }

    [Fact]
    public async Task Doctor_list_uses_the_childs_own_name()
    {
        var sub = await C.AddSubPatientAsync(Pat);
        await SeedFollowUpAsync(patientId: sub.PatientId);

        var res = await NewFollowService().DoctorGetAllFollowUpAsync(C.Id, Doc.UserId, Page());

        res.Items.Single().PatientId.ShouldBe(sub.PatientId);
        res.Items.Single().PatientName.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Doctor_list_is_paged()
    {
        for (var i = 0; i < 3; i++) await SeedFollowUpAsync(createdUtc: DateTime.UtcNow.AddMinutes(-i));

        var p2 = await NewFollowService().DoctorGetAllFollowUpAsync(C.Id, Doc.UserId, Page(2, 2));

        p2.TotalCount.ShouldBe(3);
        p2.Items.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Doctor_list_is_empty_when_there_are_none()
    {
        var res = await NewFollowService().DoctorGetAllFollowUpAsync(C.Id, Doc.UserId, Page());
        res.Items.ShouldBeEmpty();
        res.TotalCount.ShouldBe(0);
    }

    [Fact]
    public Task Doctor_list_NotFound_when_the_caller_is_not_a_doctor() =>
        ShouldFailWith("Doctor.NotFound", () => NewFollowService().DoctorGetAllFollowUpAsync(C.Id, Rec, Page()));

    [Fact]
    public Task Doctor_list_Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => NewFollowService().DoctorGetAllFollowUpAsync(999_999, Doc.UserId, Page()));

    // ------------------------------------------------------------------ Receptionist

    /// <summary>Intended behaviour: one row per doctor, with that doctor's Pending follow-ups inside. The query (GroupBy + First + nested ToList + paging) may not translate to SQL; if it throws, that is the bug.</summary>
    [Fact]
    public async Task Receptionist_sees_Pending_follow_ups_grouped_by_doctor()
    {
        var doc2 = await C.AddDoctorAsync();
        var a1 = await SeedFollowUpAsync(createdUtc: DateTime.UtcNow.AddHours(-3));
        var a2 = await SeedFollowUpAsync(createdUtc: DateTime.UtcNow.AddHours(-2));
        var b1 = await SeedFollowUpAsync(doctorId: doc2.DoctorId, createdUtc: DateTime.UtcNow.AddHours(-1));
        await SeedFollowUpAsync(status: FollowUpStatus.Booked);
        await SeedFollowUpAsync(doctorId: doc2.DoctorId, status: FollowUpStatus.Cancelled);

        var res = await NewFollowService().ReceptionistGetAllFollowUpAsync(C.Id, Rec, Page());

        res.Items.Count.ShouldBe(2);
        res.Items[0].DoctorId.ShouldBe(doc2.DoctorId);   // newest activity first
        res.Items[0].DoctorName.ShouldNotBeNullOrWhiteSpace();
        res.Items[0].FollowUps.Select(f => f.FollowUpId).ShouldBe([b1]);
        res.Items[1].DoctorId.ShouldBe(Doc.DoctorId);
        res.Items[1].FollowUps.Select(f => f.FollowUpId).OrderBy(x => x).ShouldBe(new[] { a1, a2 }.OrderBy(x => x));
        res.Items.SelectMany(i => i.FollowUps).ShouldAllBe(f => f.followUpStatus == FollowUpStatus.Pending);
    }

    [Fact]
    public async Task Receptionist_list_is_empty_when_nothing_is_Pending()
    {
        await SeedFollowUpAsync(status: FollowUpStatus.Completed);
        var res = await NewFollowService().ReceptionistGetAllFollowUpAsync(C.Id, Rec, Page());
        res.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Receptionist_list_is_paged_by_doctor()
    {
        var doc2 = await C.AddDoctorAsync();
        await SeedFollowUpAsync();
        await SeedFollowUpAsync(doctorId: doc2.DoctorId);

        var p1 = await NewFollowService().ReceptionistGetAllFollowUpAsync(C.Id, Rec, Page(1, 1));

        p1.TotalCount.ShouldBe(2);
        p1.Items.Count.ShouldBe(1);
    }

    [Fact]
    public Task Receptionist_list_NotFound_for_an_unknown_user() =>
        ShouldFailWith("Receptionist.NotFound", () => NewFollowService().ReceptionistGetAllFollowUpAsync(C.Id, 999_999, Page()));

    [Fact]
    public Task Receptionist_list_Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => NewFollowService().ReceptionistGetAllFollowUpAsync(999_999, Rec, Page()));

    // ------------------------------------------------------------------ Patient

    [Fact]
    public async Task Patient_sees_only_his_own_follow_ups_newest_first()
    {
        var other = await C.AddPatientAsync();
        var older = await SeedFollowUpAsync(createdUtc: DateTime.UtcNow.AddHours(-4));
        var newer = await SeedFollowUpAsync(createdUtc: DateTime.UtcNow.AddHours(-1));
        await SeedFollowUpAsync(patientId: other.PatientId);

        var res = await NewFollowService().PatientGetAllFollowUpAsync(C.Id, Pat.UserId, Page(), null);

        res.TotalCount.ShouldBe(2);
        res.Items.Select(i => i.Id).ToList().ShouldBe([newer, older]);
        res.Items[0].DoctorId.ShouldBe(Doc.DoctorId);
        res.Items[0].DoctorName.ShouldNotBeNullOrWhiteSpace();
        res.Items[0].Status.ShouldBe(FollowUpStatus.Pending);
    }

    /// <summary>The patient screen needs to know which visit the follow-up belongs to, and which appointment it was booked into. The projection leaves both out.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Patient_list_returns_OriginalAppointmentId_and_ScheduledAppointmentId()
    {
        var scheduled = await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date.AddDays(3), S1000);
        var id = await SeedFollowUpAsync(status: FollowUpStatus.Booked, scheduledAppointmentId: scheduled);
        var f = await LoadFollowUpAsync(id);

        var item = (await NewFollowService().PatientGetAllFollowUpAsync(C.Id, Pat.UserId, Page(), null)).Items.Single();

        item.OriginalAppointmentId.ShouldBe(f.OriginalAppointmentId);
        item.ScheduledAppointmentId.ShouldBe(scheduled);
    }

    [Fact]
    public async Task Parent_sees_a_childs_follow_ups_only_when_asking_for_that_child()
    {
        var sub = await C.AddSubPatientAsync(Pat);
        var childFollowUp = await SeedFollowUpAsync(patientId: sub.PatientId);
        await SeedFollowUpAsync();

        var res = await NewFollowService().PatientGetAllFollowUpAsync(C.Id, Pat.UserId, Page(), sub.PatientId);

        res.Items.Select(i => i.Id).ShouldBe([childFollowUp]);
    }

    [Fact]
    public async Task SubPatient_NotFoundOrNotOwned_for_someone_elses_child()
    {
        var other = await C.AddPatientAsync();
        var othersChild = await C.AddSubPatientAsync(other);
        await ShouldFailWith("SubPatient.NotFoundOrNotOwned",
            () => NewFollowService().PatientGetAllFollowUpAsync(C.Id, Pat.UserId, Page(), othersChild.PatientId));
    }

    [Fact]
    public Task Patient_list_NotFound_when_the_caller_is_not_a_patient() =>
        ShouldFailWith("Patient.NotFound", () => NewFollowService().PatientGetAllFollowUpAsync(C.Id, Rec, Page(), null));

    [Fact]
    public Task Patient_list_Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => NewFollowService().PatientGetAllFollowUpAsync(999_999, Pat.UserId, Page(), null));
}
