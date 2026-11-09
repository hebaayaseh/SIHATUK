using Sehatak.Application.DTOs.SearchDoctorDto;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.DoctorSearch;

/// <summary>The patient's "find a doctor" search.</summary>
[Collection(SqlServerCollection.Name)]
public class SearchDoctorServiceTests(SqlServerFixture fx) : DoctorSearchTestBase(fx)
{
    private static SearchDoctorRequest Req(string? name = null, int? dept = null, string? spec = null, int page = 1, int size = 50) =>
        new() { doctorName = name, departmentId = dept, Specialization = spec, PageNumber = page, PageSize = size };

    private async Task<List<int>> IdsAsync(SearchDoctorRequest r) =>
        (await NewSearch().SearchDoctorAsync(C.Id, r)).Items.Select(i => i.DoctorId).ToList();

    // ---------- by name ----------

    [Fact]
    public async Task The_name_matches_the_first_name_the_last_name_or_the_full_name_in_any_case()
    {
        var a = await AddNamedDoctorAsync("Zuhair", "Haddad");
        var b = await AddNamedDoctorAsync("Layla", "Zuhair");
        var c = await AddNamedDoctorAsync("Omar", "Qasem");

        (await IdsAsync(Req("zuhair"))).OrderBy(x => x).ShouldBe(new[] { a.DoctorId, b.DoctorId }.OrderBy(x => x));
        (await IdsAsync(Req("ZUHAIR HAD"))).ShouldBe(new[] { a.DoctorId });         // across first + last name
        (await IdsAsync(Req("qasem"))).ShouldBe(new[] { c.DoctorId });
    }

    [Fact]
    public async Task Spaces_around_the_name_are_ignored_and_a_blank_name_means_no_filter()
    {
        var a = await AddNamedDoctorAsync("Zuhair", "Haddad");

        (await IdsAsync(Req("   haddad  "))).ShouldBe(new[] { a.DoctorId });
        (await IdsAsync(Req("   "))).ShouldContain(a.DoctorId);
        (await IdsAsync(Req(null))).ShouldContain(a.DoctorId);
    }

    [Fact]
    public async Task Wildcard_characters_in_the_name_are_just_characters()
    {
        await AddNamedDoctorAsync("Zuhair", "Haddad");

        (await IdsAsync(Req("%"))).ShouldBeEmpty();
        (await IdsAsync(Req("_"))).ShouldBeEmpty();
        (await IdsAsync(Req("z_hair"))).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_name_nobody_has_gives_an_empty_page()
    {
        var r = await NewSearch().SearchDoctorAsync(C.Id, Req("nobody-has-this-name"));

        r.Items.ShouldBeEmpty();
        r.TotalCount.ShouldBe(0);
    }

    // ---------- department and specialization ----------

    [Fact]
    public async Task The_department_filter_keeps_only_that_department()
    {
        var other = await AddDepartmentAsync("Cardiology");
        var inGeneral = await AddNamedDoctorAsync("Zuhair", "A");
        var inOther = await AddNamedDoctorAsync("Zuhair", "B", departmentId: other);

        (await IdsAsync(Req("zuhair", dept: other))).ShouldBe(new[] { inOther.DoctorId });
        (await IdsAsync(Req("zuhair", dept: C.DepartmentId))).ShouldBe(new[] { inGeneral.DoctorId });
        (await IdsAsync(Req(dept: 999_999))).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_specialization_matches_part_of_the_text_in_any_case()
    {
        var heart = await AddNamedDoctorAsync("Zuhair", "A", spec: "Cardiology");
        var skin = await AddNamedDoctorAsync("Zuhair", "B", spec: "Dermatology");

        (await IdsAsync(Req("zuhair", spec: "cardio"))).ShouldBe(new[] { heart.DoctorId });
        (await IdsAsync(Req("zuhair", spec: " DERMA "))).ShouldBe(new[] { skin.DoctorId });
    }

    [Fact]
    public async Task All_the_filters_together_must_all_match()
    {
        var other = await AddDepartmentAsync("Cardiology");
        var match = await AddNamedDoctorAsync("Zuhair", "Haddad", spec: "Cardiology", departmentId: other);
        await AddNamedDoctorAsync("Zuhair", "Haddad", spec: "Dermatology", departmentId: other);   // wrong specialization
        await AddNamedDoctorAsync("Zuhair", "Haddad", spec: "Cardiology");                         // wrong department
        await AddNamedDoctorAsync("Omar", "Qasem", spec: "Cardiology", departmentId: other);       // wrong name

        (await IdsAsync(Req("zuhair", other, "cardio"))).ShouldBe(new[] { match.DoctorId });
    }

    // ---------- who is listed ----------

    [Fact]
    public async Task Deactivated_doctors_are_never_listed()
    {
        var gone = await AddNamedDoctorAsync("Zuhair", "Gone", active: false);
        var here = await AddNamedDoctorAsync("Zuhair", "Here");

        (await IdsAsync(Req("zuhair"))).ShouldBe(new[] { here.DoctorId });
        (await IdsAsync(Req())).ShouldNotContain(gone.DoctorId);
    }

    [Fact]
    public async Task Without_filters_every_active_doctor_is_listed()
    {
        var a = await AddNamedDoctorAsync("Zuhair", "A");

        var ids = await IdsAsync(Req());

        ids.ShouldContain(a.DoctorId);
        ids.ShouldContain(Doc.DoctorId);
    }

    [Fact]
    public async Task A_doctor_of_another_center_is_not_found()
    {
        var other = await Fx.Harness.CreateCenterAsync();
        await AddNamedDoctorAsync("Zuhair", "Here");

        var r = await NewSearch().SearchDoctorAsync(other.Id, Req("zuhair"));

        r.Items.ShouldBeEmpty();
    }

    // ---------- the card ----------

    [Fact]
    public async Task The_card_has_name_specialization_bio_and_online_flag()
    {
        var d = await AddNamedDoctorAsync("Zuhair", "Haddad", spec: "Cardiology");

        var item = (await NewSearch().SearchDoctorAsync(C.Id, Req("zuhair"))).Items.ShouldHaveSingleItem();

        item.DoctorId.ShouldBe(d.DoctorId);
        item.DoctorName.ShouldBe("Zuhair Haddad");
        item.Specialization.ShouldBe("Cardiology");
        item.Bio.ShouldBe("bio");
        item.OnlineEnabled.ShouldBeFalse();
    }

    [Fact]
    public async Task Only_the_active_working_hours_are_shown()
    {
        var d = await AddNamedDoctorAsync("Zuhair", "Haddad");
        await C.AddScheduleAsync(d.DoctorId, DayOfWeek.Tuesday, new TimeOnly(9, 0), new TimeOnly(13, 0), slotMinutes: 20);
        await C.AddScheduleAsync(d.DoctorId, DayOfWeek.Wednesday, new TimeOnly(9, 0), new TimeOnly(13, 0), isActive: false);

        var item = (await NewSearch().SearchDoctorAsync(C.Id, Req("zuhair"))).Items.ShouldHaveSingleItem();

        var s = item.doctorSchedule.ShouldHaveSingleItem();
        s.StartTime.ShouldBe(new TimeOnly(9, 0));
        s.EndTime.ShouldBe(new TimeOnly(13, 0));
        s.SlotDurationMinutes.ShouldBe(20);
        s.IsActive.ShouldBeTrue();
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Each_working_hours_row_says_which_day_it_is_for()
    {
        var d = await AddNamedDoctorAsync("Zuhair", "Haddad");
        await C.AddScheduleAsync(d.DoctorId, DayOfWeek.Tuesday, new TimeOnly(9, 0), new TimeOnly(13, 0));

        var item = (await NewSearch().SearchDoctorAsync(C.Id, Req("zuhair"))).Items.ShouldHaveSingleItem();

        item.doctorSchedule.ShouldHaveSingleItem().DayOfWeek.ShouldBe(DayOfWeek.Tuesday);   // never mapped: always Sunday
    }

    [Fact]
    public async Task A_doctor_without_hours_has_an_empty_schedule_not_null()
    {
        await AddNamedDoctorAsync("Zuhair", "Haddad");

        (await NewSearch().SearchDoctorAsync(C.Id, Req("zuhair"))).Items.ShouldHaveSingleItem().doctorSchedule.ShouldBeEmpty();
    }

    // ---------- order and paging ----------

    [Fact]
    public async Task Doctors_come_in_first_name_order()
    {
        var c = await AddNamedDoctorAsync("Zk-C", "X");
        var a = await AddNamedDoctorAsync("Zk-A", "X");
        var b = await AddNamedDoctorAsync("Zk-B", "X");

        (await IdsAsync(Req("zk-"))).ShouldBe(new[] { a.DoctorId, b.DoctorId, c.DoctorId });
    }

    [Fact]
    public async Task Paging_walks_through_every_doctor_once()
    {
        for (var i = 1; i <= 5; i++) await AddNamedDoctorAsync($"Zk-{i}", "X");

        var p1 = await NewSearch().SearchDoctorAsync(C.Id, Req("zk-", page: 1, size: 2));
        var p2 = await NewSearch().SearchDoctorAsync(C.Id, Req("zk-", page: 2, size: 2));
        var p3 = await NewSearch().SearchDoctorAsync(C.Id, Req("zk-", page: 3, size: 2));

        p1.TotalCount.ShouldBe(5);
        new[] { p1.Items.Count, p2.Items.Count, p3.Items.Count }.ShouldBe(new[] { 2, 2, 1 });
        p1.Items.Concat(p2.Items).Concat(p3.Items).Select(i => i.DoctorId).Distinct().Count().ShouldBe(5);
    }

    [Fact]
    public async Task Doctors_with_the_same_first_name_do_not_repeat_or_vanish_between_pages()
    {
        // order by first name alone leaves ties in an undefined order, so a page can overlap the next one
        for (var i = 1; i <= 6; i++) await AddNamedDoctorAsync("Zk-Same", $"L{i}");

        var seen = new List<int>();
        for (var page = 1; page <= 3; page++)
            seen.AddRange((await NewSearch().SearchDoctorAsync(C.Id, Req("zk-same", page: page, size: 2))).Items.Select(i => i.DoctorId));

        seen.Count.ShouldBe(6);
        seen.Distinct().Count().ShouldBe(6);
    }

    // ---------- center ----------

    [Fact]
    public async Task An_unknown_or_suspended_center_fails()
    {
        await ShouldFailWith("Center.NotFound", () => NewSearch().SearchDoctorAsync(999_999, Req()));

        var suspended = await Fx.Harness.CreateCenterAsync(CenterStatus.Suspended);
        await ShouldFailWith("Center.NotFound", () => NewSearch().SearchDoctorAsync(suspended.Id, Req()));
    }
}
