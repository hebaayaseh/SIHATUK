using Microsoft.EntityFrameworkCore;
using Sehatak.IntegrationTests.Infrastructure;
using Sehatak.Application.DTOs.DepartmentDto;
using Shouldly;

namespace Sehatak.IntegrationTests.Departments;

[Collection(SqlServerCollection.Name)]
public class DepartmentsServiceTests(SqlServerFixture fx) : DepartmentsTestBase(fx)
{
    // ---------- Add ----------

    [Fact]
    public async Task Add_creates_the_department_and_audits_it_with_the_real_id()
    {
        var r = await NewDepartments().AddDepartmentAsync(C.Id, Dept("Pediatrics", "kids"));

        r.departmentId.ShouldBeGreaterThan(0);
        r.departmentName.ShouldBe("Pediatrics");

        await using var db = Db();
        var row = await db.Departments.AsNoTracking().SingleAsync(d => d.Id == r.departmentId);
        row.Description.ShouldBe("kids");
        (await db.AuditLogs.AsNoTracking().SingleAsync(a => a.Action == "AddDepartment")).EntityId.ShouldBe(r.departmentId);
    }

    [Fact]
    public Task Add_duplicate_name_is_rejected() =>
        ShouldFailWith("Department.Exist", () => NewDepartments().AddDepartmentAsync(C.Id, Dept("General")));

    [Fact]
    public async Task Add_on_unknown_center_is_rejected() =>
        await ShouldFailWith("Center.NotFound", () => NewDepartments().AddDepartmentAsync(999_999, Dept("X")));

    [Fact]
    public async Task Add_on_suspended_center_is_rejected()
    {
        var suspended = await Fx.Harness.CreateCenterAsync(Sehatak.Domain.Enums.SharedEnums.CenterStatus.Suspended);
        var svc = new Sehatak.Infrastructure.Services.DepartmentService.DepartmentsService(
            Fx.Harness.TenantFactory, Fx.Harness.NewSharedDb(), Email, Cache, suspended.Audit);
        await ShouldFailWith("Center.NotFound", () => svc.AddDepartmentAsync(suspended.Id, Dept("X")));
    }

    // ---------- Update ----------

    [Fact]
    public async Task Update_changes_only_the_fields_sent()
    {
        var id = await AddDepartmentRowAsync("Old");
        var r = await NewDepartments().UpdateDepartmentAsync(C.Id,
            new DepartmentUpdateRequestDto { departmentId = id, departmentdiscription = "new desc" });

        r.departmentName.ShouldBe("Old");
        await using var db = Db();
        var row = await db.Departments.AsNoTracking().SingleAsync(d => d.Id == id);
        row.Name.ShouldBe("Old");
        row.Description.ShouldBe("new desc");
        (await db.AuditLogs.AsNoTracking().SingleAsync(a => a.Action == "UpdateDepartment")).EntityId.ShouldBe(id);
    }

    [Fact]
    public Task Update_unknown_department_is_rejected() =>
        ShouldFailWith("Department.NotFound", () => NewDepartments().UpdateDepartmentAsync(C.Id,
            new DepartmentUpdateRequestDto { departmentId = 999_999, departmentName = "X" }));

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Update_to_a_name_used_by_another_department_is_rejected()
    {
        var id = await AddDepartmentRowAsync("Dermatology");
        await ShouldFailWith("Department.Exist", () => NewDepartments().UpdateDepartmentAsync(C.Id,
            new DepartmentUpdateRequestDto { departmentId = id, departmentName = "General" }));
    }

    [Fact]
    public async Task Update_keeping_its_own_name_is_allowed()
    {
        var id = await AddDepartmentRowAsync("Dermatology");
        var r = await NewDepartments().UpdateDepartmentAsync(C.Id,
            new DepartmentUpdateRequestDto { departmentId = id, departmentName = "Dermatology", departmentdiscription = "x" });
        r.departmentName.ShouldBe("Dermatology");
    }

    // ---------- Remove ----------

    [Fact]
    public async Task Remove_deletes_an_empty_department_and_audits()
    {
        var id = await AddDepartmentRowAsync("Temp");
        (await NewDepartments().RemoveDepartmentAsync(C.Id, new DepartmentRemoveRequestDto { departmentId = id }))
            .ShouldBe("Department.Removed");

        await using var db = Db();
        (await db.Departments.AnyAsync(d => d.Id == id)).ShouldBeFalse();
        (await db.AuditLogs.AsNoTracking().SingleAsync(a => a.Action == "RemoveDepartment")).EntityId.ShouldBe(id);
    }

    [Fact]
    public Task Remove_unknown_department_is_rejected() =>
        ShouldFailWith("Department.NotFound", () => NewDepartments().RemoveDepartmentAsync(C.Id,
            new DepartmentRemoveRequestDto { departmentId = 999_999 }));

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Remove_department_that_has_doctors_gives_a_business_error_not_a_crash()
    {
        // Doc (from the base) belongs to the "General" department
        await ShouldFailWith("Department.HasDoctors", () => NewDepartments().RemoveDepartmentAsync(C.Id,
            new DepartmentRemoveRequestDto { departmentId = C.DepartmentId }));

        await using var db = Db();
        (await db.Departments.AnyAsync(d => d.Id == C.DepartmentId)).ShouldBeTrue();
    }

    // ---------- Get / cache ----------

    [Fact]
    public async Task Get_lists_departments_with_their_logo()
    {
        await using (var db = Db())
        {
            db.Departments.Add(new Sehatak.Domain.Entities.TenantEntities.Department { Name = "WithLogo", ImageUrl = "/uploads/x.png" });
            await db.SaveChangesAsync();
        }

        var list = (await NewDepartments().GetDepartmentsAsync(C.Id)).Departments;
        list.ShouldContain(d => d.departmentName == "General");
        list.Single(d => d.departmentName == "WithLogo").logo.ShouldBe("/uploads/x.png");
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Get_shows_a_department_right_after_it_is_added()
    {
        var svc = NewDepartments();
        await svc.GetDepartmentsAsync(C.Id);                 // fills the cache
        await svc.AddDepartmentAsync(C.Id, Dept("Neurology"));

        (await svc.GetDepartmentsAsync(C.Id)).Departments.ShouldContain(d => d.departmentName == "Neurology");
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Get_shows_the_new_name_right_after_an_update()
    {
        var svc = NewDepartments();
        var id = await AddDepartmentRowAsync("Before");
        await svc.GetDepartmentsAsync(C.Id);
        await svc.UpdateDepartmentAsync(C.Id, new DepartmentUpdateRequestDto { departmentId = id, departmentName = "After" });

        var names = (await svc.GetDepartmentsAsync(C.Id)).Departments.Select(d => d.departmentName).ToList();
        names.ShouldContain("After");
        names.ShouldNotContain("Before");
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Get_does_not_show_a_department_right_after_it_is_removed()
    {
        var svc = NewDepartments();
        var id = await AddDepartmentRowAsync("Gone");
        await svc.GetDepartmentsAsync(C.Id);
        await svc.RemoveDepartmentAsync(C.Id, new DepartmentRemoveRequestDto { departmentId = id });

        (await svc.GetDepartmentsAsync(C.Id)).Departments.ShouldNotContain(d => d.departmentName == "Gone");
    }

    [Fact]
    public async Task Get_on_unknown_center_is_rejected() =>
        await ShouldFailWith("Center.NotFound", () => NewDepartments().GetDepartmentsAsync(999_999));
}
