using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Sehatak.Application.DTOs.DepartmentDto;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Domain.Entities.SharedEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Departments;

[Collection(SqlServerCollection.Name)]
public class StaffServiceTests(SqlServerFixture fx) : DepartmentsTestBase(fx)
{
    // ---------- Add ----------

    [Theory]
    [InlineData(userRole.Receptionist)]
    [InlineData(userRole.Nurse)]
    [InlineData(userRole.LabTechnician)]
    [InlineData(userRole.GeneralDoctor)]
    public async Task Admin_creates_an_active_staff_member_with_the_requested_role(userRole role)
    {
        var req = StaffReq(role);
        var r = await NewStaff().AddStafAsync(C.AdminUserId, C.Id, req);

        var user = await UserByEmailAsync(req.email);
        user!.Id.ShouldBe(r.UserId);
        user.role.ShouldBe(role);
        user.isActive.ShouldBeTrue();
        await Email.Received(1).SendTempPasswordAsync(req.email, "Ali Staff", Arg.Any<string>(), Arg.Any<string>());
    }

    [Theory]
    [InlineData(userRole.SuperAdmin)]
    [InlineData(userRole.Patient)]
    [InlineData(userRole.Doctor)]
    public async Task Forbidden_roles_cannot_be_created_here(userRole role)
    {
        var req = StaffReq(role);
        await ShouldFailWith("Auth.Forbidden", () => NewStaff().AddStafAsync(C.AdminUserId, C.Id, req));
        (await UserByEmailAsync(req.email)).ShouldBeNull();
    }

    [Fact]
    public async Task Unknown_or_inactive_requester_is_forbidden()
    {
        await ShouldFailWith("Auth.Forbidden", () => NewStaff().AddStafAsync(999_999, C.Id, StaffReq(userRole.Nurse)));

        var inactive = await C.AddReceptionistAsync(active: false);
        await ShouldFailWith("Auth.Forbidden", () => NewStaff().AddStafAsync(inactive, C.Id, StaffReq(userRole.Nurse)));
    }

    [Fact]
    public async Task Existing_email_is_rejected()
    {
        var req = StaffReq(userRole.Nurse);
        await NewStaff().AddStafAsync(C.AdminUserId, C.Id, req);
        await ShouldFailWith("Auth.EmailExists", () => NewStaff().AddStafAsync(C.AdminUserId, C.Id, StaffReq(userRole.Nurse, req.email)));
    }

    [Fact]
    public Task Unknown_center_is_rejected() =>
        ShouldFailWith("Center.NotFound", () => NewStaff().AddStafAsync(C.AdminUserId, 999_999, StaffReq(userRole.Nurse)));

    [Fact]
    public async Task Audit_entry_has_the_new_id_and_no_password()
    {
        var req = StaffReq(userRole.Nurse);
        var r = await NewStaff().AddStafAsync(C.AdminUserId, C.Id, req);

        await using var db = Db();
        var audit = await db.AuditLogs.AsNoTracking().SingleAsync(a => a.Action == "CreateStaff");
        audit.EntityId.ShouldBe(r.UserId);
        C.Audit.Received().Build("CreateStaff", "User", r.UserId, Arg.Any<object?>(),
            Arg.Is<object?>(o => o != null && !o.ToString()!.ToLower().Contains("password")));
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task If_the_email_cannot_be_sent_no_half_account_is_left_behind()
    {
        Email.SendTempPasswordAsync(default!, default!, default!, default!).ReturnsForAnyArgs(Task.FromException(new InvalidOperationException("smtp down")));
        var req = StaffReq(userRole.Nurse);

        await Should.ThrowAsync<Exception>(() => NewStaff().AddStafAsync(C.AdminUserId, C.Id, req));

        (await UserByEmailAsync(req.email)).ShouldBeNull();
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Two_requests_with_the_same_email_at_once_give_one_account_and_one_business_error()
    {
        var email = NewEmail();
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            try { await NewStaff().AddStafAsync(C.AdminUserId, C.Id, StaffReq(userRole.Nurse, email)); return (Exception?)null; }
            catch (Exception e) { return e; }
        }));

        results.Count(e => e == null).ShouldBe(1);
        results.Single(e => e != null).ShouldBeOfType<BusinessException>();
    }

    // ---------- Deactivate / activate ----------

    [Fact]
    public async Task Deactivate_then_activate_toggles_the_flag_and_audits_both()
    {
        var id = await C.AddReceptionistAsync();
        var svc = NewStaff();

        (await svc.RemoveStaffAsync(C.Id, new RemoveStaffRequestDto { userId = id })).ShouldBeTrue();
        await using (var db = Db()) (await db.Users.AsNoTracking().SingleAsync(u => u.Id == id)).isActive.ShouldBeFalse();

        (await svc.ActiveStaffAsync(C.Id, new RemoveStaffRequestDto { userId = id })).ShouldBeTrue();
        await using (var db = Db())
        {
            (await db.Users.AsNoTracking().SingleAsync(u => u.Id == id)).isActive.ShouldBeTrue();
            (await db.AuditLogs.AsNoTracking().CountAsync(a => a.EntityId == id && (a.Action == "DeactivateStaff" || a.Action == "ActivateStaff"))).ShouldBe(2);
        }
    }

    // ---------- Admin rules (decided by the owner) ----------

    [Fact]
    public async Task Admin_can_create_another_admin()
    {
        var req = StaffReq(userRole.Admin);
        await NewStaff().AddStafAsync(C.AdminUserId, C.Id, req);
        (await UserByEmailAsync(req.email))!.role.ShouldBe(userRole.Admin);
    }

    [Fact]
    public async Task Admin_can_deactivate_himself()
    {
        (await NewStaff().RemoveStaffAsync(C.Id, new RemoveStaffRequestDto { userId = C.AdminUserId })).ShouldBeTrue();
        await using var db = Db();
        (await db.Users.AsNoTracking().SingleAsync(u => u.Id == C.AdminUserId)).isActive.ShouldBeFalse();
    }

    // ---------- Refresh tokens ----------

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Deactivating_staff_revokes_all_their_refresh_tokens_but_not_other_users()
    {
        var target = await C.AddReceptionistAsync();
        var other = await C.AddReceptionistAsync();
        await using (var shared = Fx.Harness.NewSharedDb())
        {
            shared.RefreshTokens.AddRange(
                Token(target, "t1"), Token(target, "t2"), Token(other, "o1"));
            await shared.SaveChangesAsync();
        }

        await NewStaff().RemoveStaffAsync(C.Id, new RemoveStaffRequestDto { userId = target });

        await using var check = Fx.Harness.NewSharedDb();
        (await check.RefreshTokens.AsNoTracking().Where(t => t.UserId == target && t.CenterId == C.Id).AllAsync(t => t.IsRevoked)).ShouldBeTrue();
        (await check.RefreshTokens.AsNoTracking().SingleAsync(t => t.UserId == other && t.CenterId == C.Id)).IsRevoked.ShouldBeFalse();
    }

    private RefreshToken Token(int userId, string value) => new()
    {
        Token = $"{value}-{Guid.NewGuid():N}", OwnerType = TokenOwnerType.TenantUser,
        UserId = userId, CenterId = C.Id, ExpiresAt = DateTime.UtcNow.AddDays(7)
    };

    [Fact]
    public async Task Unknown_user_is_not_found()
    {
        await ShouldFailWith("User.NotFound", () => NewStaff().RemoveStaffAsync(C.Id, new RemoveStaffRequestDto { userId = 999_999 }));
        await ShouldFailWith("User.NotFound", () => NewStaff().ActiveStaffAsync(C.Id, new RemoveStaffRequestDto { userId = 999_999 }));
    }

    [Fact]
    public async Task A_patient_cannot_be_toggled_as_staff()
    {
        await ShouldFailWith("Auth.Forbidden", () => NewStaff().RemoveStaffAsync(C.Id, new RemoveStaffRequestDto { userId = Pat.UserId }));
        await ShouldFailWith("Auth.Forbidden", () => NewStaff().ActiveStaffAsync(C.Id, new RemoveStaffRequestDto { userId = Pat.UserId }));
    }

    [Fact]
    public async Task Toggling_on_unknown_center_is_rejected()
    {
        await ShouldFailWith("Center.NotFound", () => NewStaff().RemoveStaffAsync(999_999, new RemoveStaffRequestDto { userId = 1 }));
        await ShouldFailWith("Center.NotFound", () => NewStaff().ActiveStaffAsync(999_999, new RemoveStaffRequestDto { userId = 1 }));
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Toggling_on_a_suspended_center_is_rejected_like_every_other_method()
    {
        var suspended = await Fx.Harness.CreateCenterAsync(Sehatak.Domain.Enums.SharedEnums.CenterStatus.Suspended);
        var svc = NewStaff();
        await ShouldFailWith("Center.NotFound", () => svc.RemoveStaffAsync(suspended.Id, new RemoveStaffRequestDto { userId = suspended.AdminUserId }));
    }
}
