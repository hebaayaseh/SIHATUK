using Sehatak.Application.Common;
using Sehatak.Domain.Enums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Emergency;

/// <summary>The receptionist's two screens: the list of emergency cases and the list of general doctors on shift today.</summary>
[Collection(SqlServerCollection.Name)]
public class EmergencyQueryTests(SqlServerFixture fx) : EmergencyTestBase(fx)
{
    private static PagedRequest Page(int n = 1, int size = 10) => new() { PageNumber = n, PageSize = size };

    // ------------------------------------------------------------------ cases

    [Fact]
    public async Task Cases_list_returns_the_fields_and_the_total()
    {
        var id = await SeedEmergencyAsync("Ali", insured: true, amount: 0m);

        var res = await NewEmergencyService().GetPatientEmergencyAsync(C.Id, Page());

        res.TotalCount.ShouldBe(1);
        var i = res.Items.Single();
        i.Id.ShouldBe(id);
        i.PatientName.ShouldBe("Ali");
        i.DoctorUserId.ShouldBe(GenDoc);
        i.ReceptionistId.ShouldBe(Rec);
        i.IsInsurance.ShouldBeTrue();
        i.AmountPaid.ShouldBe(0m);
    }

    [Fact]
    public async Task Cases_list_is_empty_when_there_are_none()
    {
        var res = await NewEmergencyService().GetPatientEmergencyAsync(C.Id, Page());
        res.Items.ShouldBeEmpty();
        res.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task Cases_list_is_paged()
    {
        for (var i = 0; i < 5; i++) await SeedEmergencyAsync($"Patient {i}", createdUtc: DateTime.UtcNow.AddMinutes(-i));

        var p3 = await NewEmergencyService().GetPatientEmergencyAsync(C.Id, Page(3, 2));

        p3.TotalCount.ShouldBe(5);
        p3.Items.Count.ShouldBe(1);
    }

    /// <summary>Today it is OrderBy(CreatedAt) ascending: page 1 is always the oldest cases, so after a few months the receptionist never sees today's cases on the first page.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Cases_list_shows_the_newest_case_first()
    {
        var oldest = await SeedEmergencyAsync("Oldest", createdUtc: DateTime.UtcNow.AddDays(-30));
        var middle = await SeedEmergencyAsync("Middle", createdUtc: DateTime.UtcNow.AddDays(-5));
        var newest = await SeedEmergencyAsync("Newest", createdUtc: DateTime.UtcNow.AddMinutes(-1));

        var res = await NewEmergencyService().GetPatientEmergencyAsync(C.Id, Page());

        res.Items.Select(i => i.Id).ToList().ShouldBe([newest, middle, oldest]);
    }

    [Fact]
    public Task Cases_list_Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => NewEmergencyService().GetPatientEmergencyAsync(999_999, Page()));

    // ------------------------------------------------------------------ general doctors on shift

    [Fact]
    public async Task Doctors_list_returns_the_general_doctor_on_shift_today()
    {
        var res = await NewEmergencyService().GetDoctorGeneralAsync(C.Id, Page());

        var d = res.Items.Single();
        d.DoctorUserId.ShouldBe(GenDoc);
        d.DoctorName.ShouldNotBeNullOrWhiteSpace();
        d.Email.ShouldNotBeNullOrWhiteSpace();
        d.PhoneNumber.ShouldNotBeNullOrWhiteSpace();
        d.Date.ShouldBe(ClinicClock.Today);
        d.ShiftName.ShouldBe(ShiftGroup.A);
    }

    [Fact]
    public async Task Doctors_list_leaves_out_absent_on_leave_other_days_and_other_roles()
    {
        var absent = await AddGeneralDoctorAsync();
        await AddAttendanceAsync(absent, AttendanceStatus.Absent);
        var onLeave = await AddGeneralDoctorAsync();
        await AddAttendanceAsync(onLeave, AttendanceStatus.OnLeave);
        var yesterday = await AddGeneralDoctorAsync();
        await AddAttendanceAsync(yesterday, AttendanceStatus.Present, ClinicClock.Today.AddDays(-1));
        await AddAttendanceAsync(Doc.UserId);                       // a specialist on shift today
        var late = await AddGeneralDoctorAsync();
        await AddAttendanceAsync(late, AttendanceStatus.Late, group: ShiftGroup.B);

        var res = await NewEmergencyService().GetDoctorGeneralAsync(C.Id, Page());

        res.Items.Select(i => i.DoctorUserId).OrderBy(x => x).ShouldBe(new[] { GenDoc, late }.OrderBy(x => x));
        res.TotalCount.ShouldBe(2);
    }

    /// <summary>Register refuses an inactive doctor (Doctor.NotFound) but this list still shows him, so the receptionist picks him and gets an error.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Doctors_list_leaves_out_inactive_doctors()
    {
        var inactive = await AddGeneralDoctorAsync(active: false);
        await AddAttendanceAsync(inactive);

        var res = await NewEmergencyService().GetDoctorGeneralAsync(C.Id, Page());

        res.Items.Select(i => i.DoctorUserId).ShouldNotContain(inactive);
    }

    /// <summary>Every page must give different doctors and together all of them (the query orders by AttendanceDate, which is the same for everyone).</summary>
    [Fact]
    public async Task Doctors_list_paging_returns_every_doctor_exactly_once()
    {
        for (var i = 0; i < 4; i++) await AddAttendanceAsync(await AddGeneralDoctorAsync());   // 5 in total with GenDoc

        var seen = new List<int>();
        for (var p = 1; p <= 3; p++)
            seen.AddRange((await NewEmergencyService().GetDoctorGeneralAsync(C.Id, Page(p, 2))).Items.Select(i => i.DoctorUserId));

        seen.Count.ShouldBe(5);
        seen.Distinct().Count().ShouldBe(5);
    }

    [Fact]
    public Task Doctors_list_Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => NewEmergencyService().GetDoctorGeneralAsync(999_999, Page()));
}
