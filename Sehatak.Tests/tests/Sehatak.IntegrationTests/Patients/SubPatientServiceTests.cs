using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.SubPatientDto;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Patients;

[Collection(SqlServerCollection.Name)]
public class SubPatientServiceTests(SqlServerFixture fx) : PatientsTestBase(fx)
{
    private static SummarySubPatientRequestDto Kid(string first, string last = "Kid") => new()
    {
        SubPatientFirstName = first, SubPatientLastName = last, BloodType = BloodType.A_Positive,
        WhatAppNumber = "0591111111", Gender = Gender.Female, DateOfBith = new DateOnly(2018, 5, 5)
    };

    private static AddSubPatientRequestDto Kids(params string[] names) =>
        new() { SubPatients = names.Select(n => Kid(n)).ToList() };

    // ---------- Add ----------

    [Fact]
    public async Task Add_creates_children_owned_by_the_parent_without_a_login_of_their_own()
    {
        var r = await NewSubPatients().AddSubPatientAsync(C.Id, Pat.UserId, Kids("Lina", "Omar"));

        r.Count.ShouldBe(2);
        r.ShouldAllBe(x => x.Id > 0);

        foreach (var dto in r)
        {
            var row = await PatientRowAsync(dto.Id);
            row.ParentPatientId.ShouldBe(Pat.PatientId);
            row.userId.ShouldBeNull();
            row.NotifiableUserId.ShouldBe(Pat.UserId);   // notifications about the child go to the parent
        }
    }

    [Fact]
    public async Task Add_keeps_every_field_the_caller_sent()
    {
        var r = await NewSubPatients().AddSubPatientAsync(C.Id, Pat.UserId, Kids("Lina"));

        var row = await PatientRowAsync(r.Single().Id);
        row.FirstName.ShouldBe("Lina");
        row.LastName.ShouldBe("Kid");
        row.DateOfBith.ShouldBe(new DateOnly(2018, 5, 5));
        row.WhatsappNumber.ShouldBe("0591111111");
        row.BloodType.ShouldBe(BloodType.A_Positive);
        row.Gender.ShouldBe(Gender.Female);
    }

    [Fact]
    public async Task Add_notifies_the_parent_and_writes_an_audit_row()
    {
        await NewSubPatients().AddSubPatientAsync(C.Id, Pat.UserId, Kids("Lina"));

        await using var db = Db();
        (await db.Notifications.CountAsync(n => n.UserId == Pat.UserId)).ShouldBe(1);
        (await db.AuditLogs.CountAsync(a => a.Action == "AddSubPatient")).ShouldBe(1);
    }

    [Fact]
    public async Task Add_with_an_empty_list_returns_nothing_and_writes_nothing()
    {
        var r = await NewSubPatients().AddSubPatientAsync(C.Id, Pat.UserId, new AddSubPatientRequestDto { SubPatients = new() });

        r.ShouldBeEmpty();
        await using var db = Db();
        (await db.Patients.CountAsync(p => p.ParentPatientId != null)).ShouldBe(0);
        (await db.Notifications.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Add_to_an_unknown_or_suspended_center_fails()
    {
        await ShouldFailWith("Center.NotFound",
            () => NewSubPatients().AddSubPatientAsync(999_999, Pat.UserId, Kids("Lina")));

        var suspended = await Fx.Harness.CreateCenterAsync(CenterStatus.Suspended);
        await ShouldFailWith("Center.NotFound",
            () => NewSubPatients().AddSubPatientAsync(suspended.Id, Pat.UserId, Kids("Lina")));
    }

    [Fact]
    public async Task Add_is_only_for_patients_who_are_active()
    {
        await ShouldFailWith("Patient.NotFound",
            () => NewSubPatients().AddSubPatientAsync(C.Id, Doc.UserId, Kids("Lina")));   // a doctor is not a patient

        var gone = await C.AddPatientAsync(active: false);
        await ShouldFailWith("Patient.NotFound",
            () => NewSubPatients().AddSubPatientAsync(C.Id, gone.UserId, Kids("Lina")));
    }

    // ---------- Get ----------

    [Fact]
    public async Task Get_returns_only_my_children_ordered_by_first_name()
    {
        var other = await C.AddPatientAsync();
        await NewSubPatients().AddSubPatientAsync(C.Id, Pat.UserId, Kids("Zaid", "Amal", "Mona"));
        await NewSubPatients().AddSubPatientAsync(C.Id, other.UserId, Kids("Hidden"));

        var page = await NewSubPatients().GetSubPatientsAsync(C.Id, Pat.UserId, new PagedRequest());

        page.Items.Select(x => x.SubPatientFirstName).ShouldBe(new[] { "Amal", "Mona", "Zaid" });
    }

    [Fact]
    public async Task Get_pages_the_result()
    {
        await NewSubPatients().AddSubPatientAsync(C.Id, Pat.UserId, Kids("A", "B", "C"));

        var p1 = await NewSubPatients().GetSubPatientsAsync(C.Id, Pat.UserId, new PagedRequest { PageNumber = 1, PageSize = 2 });
        var p2 = await NewSubPatients().GetSubPatientsAsync(C.Id, Pat.UserId, new PagedRequest { PageNumber = 2, PageSize = 2 });

        p1.Items.Count.ShouldBe(2);
        p2.Items.Count.ShouldBe(1);
        p1.TotalCount.ShouldBe(3);
    }

    [Fact]
    public async Task Get_for_a_patient_without_children_is_an_empty_page()
    {
        var page = await NewSubPatients().GetSubPatientsAsync(C.Id, Pat.UserId, new PagedRequest());
        page.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Get_fails_for_a_non_patient_user()
    {
        await ShouldFailWith("Patient.NotFound",
            () => NewSubPatients().GetSubPatientsAsync(C.Id, Doc.UserId, new PagedRequest()));
    }

    // ---------- Update ----------

    [Fact]
    public async Task Update_changes_only_the_fields_that_were_sent()
    {
        var kid = (await NewSubPatients().AddSubPatientAsync(C.Id, Pat.UserId, Kids("Lina"))).Single();

        var r = await NewSubPatients().UpdateSubPatientAsync(C.Id, Pat.UserId, kid.Id,
            new UpdateSubPatientRequestDto { SubPatientFirstName = "Lena", BloodType = BloodType.O_Negative });

        r.SubPatientFirstName.ShouldBe("Lena");
        r.BloodType.ShouldBe(BloodType.O_Negative);

        var row = await PatientRowAsync(kid.Id);
        row.FirstName.ShouldBe("Lena");
        row.BloodType.ShouldBe(BloodType.O_Negative);
        row.LastName.ShouldBe("Kid");                          // untouched
        row.WhatsappNumber.ShouldBe("0591111111");
        row.Gender.ShouldBe(Gender.Female);
        row.DateOfBith.ShouldBe(new DateOnly(2018, 5, 5));
        row.ParentPatientId.ShouldBe(Pat.PatientId);
    }

    [Fact]
    public async Task Update_an_empty_request_changes_nothing()
    {
        var kid = (await NewSubPatients().AddSubPatientAsync(C.Id, Pat.UserId, Kids("Lina"))).Single();

        await NewSubPatients().UpdateSubPatientAsync(C.Id, Pat.UserId, kid.Id, new UpdateSubPatientRequestDto());

        var row = await PatientRowAsync(kid.Id);
        row.FirstName.ShouldBe("Lina");
        row.LastName.ShouldBe("Kid");
    }

    [Fact]
    public async Task Update_writes_an_audit_row()
    {
        var kid = (await NewSubPatients().AddSubPatientAsync(C.Id, Pat.UserId, Kids("Lina"))).Single();
        await NewSubPatients().UpdateSubPatientAsync(C.Id, Pat.UserId, kid.Id, new UpdateSubPatientRequestDto { SubPatientFirstName = "Lena" });

        await using var db = Db();
        (await db.AuditLogs.CountAsync(a => a.Action == "UpdateSubPatient")).ShouldBe(1);
    }

    [Fact]
    public async Task Update_cannot_touch_a_child_that_belongs_to_another_parent()
    {
        var other = await C.AddPatientAsync();
        var theirs = (await NewSubPatients().AddSubPatientAsync(C.Id, other.UserId, Kids("Theirs"))).Single();

        await ShouldFailWith("SubPatient.NotFound", () => NewSubPatients().UpdateSubPatientAsync(
            C.Id, Pat.UserId, theirs.Id, new UpdateSubPatientRequestDto { SubPatientFirstName = "Hacked" }));

        (await PatientRowAsync(theirs.Id)).FirstName.ShouldBe("Theirs");
    }

    [Fact]
    public async Task Update_cannot_be_used_to_edit_a_registered_patient_through_the_child_endpoint()
    {
        var other = await C.AddPatientAsync();

        await ShouldFailWith("SubPatient.NotFound", () => NewSubPatients().UpdateSubPatientAsync(
            C.Id, Pat.UserId, other.PatientId, new UpdateSubPatientRequestDto { SubPatientFirstName = "Hacked" }));
        await ShouldFailWith("SubPatient.NotFound", () => NewSubPatients().UpdateSubPatientAsync(
            C.Id, Pat.UserId, Pat.PatientId, new UpdateSubPatientRequestDto { SubPatientFirstName = "Me" }));
    }

    [Fact]
    public async Task Update_an_unknown_child_fails()
    {
        await ShouldFailWith("SubPatient.NotFound", () => NewSubPatients().UpdateSubPatientAsync(
            C.Id, Pat.UserId, 999_999, new UpdateSubPatientRequestDto { SubPatientFirstName = "X" }));
    }
}
