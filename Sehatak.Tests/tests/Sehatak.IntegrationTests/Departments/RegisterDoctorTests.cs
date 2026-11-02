using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Domain.Enums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Departments;

[Collection(SqlServerCollection.Name)]
public class RegisterDoctorTests(SqlServerFixture fx) : DepartmentsTestBase(fx)
{
    [Fact]
    public async Task Creates_an_active_Doctor_user_and_doctor_row()
    {
        var req = DoctorReq();
        var r = await NewDepartments().RegisterDoctorAsync(C.Id, req);

        r.Email.ShouldBe(req.email);
        await using var db = Db();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == r.UserId);
        user.role.ShouldBe(userRole.Doctor);
        user.isActive.ShouldBeTrue();
        var doctor = await db.Doctors.AsNoTracking().SingleAsync(d => d.userId == r.UserId);
        doctor.departmentId.ShouldBe(C.DepartmentId);
        doctor.Specialization.ShouldBe("Cardiology");
        doctor.OnlineEnabled.ShouldBeTrue();
    }

    [Fact]
    public async Task Emails_the_temp_password_which_is_stored_only_as_a_hash()
    {
        string? sent = null;
        Email.SendTempPasswordAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Do<string>(p => sent = p), Arg.Any<string>())
             .Returns(Task.CompletedTask);

        var req = DoctorReq();
        await NewDepartments().RegisterDoctorAsync(C.Id, req);

        sent.ShouldNotBeNull();
        sent!.Length.ShouldBeGreaterThanOrEqualTo(8);
        var user = await UserByEmailAsync(req.email);
        user!.passwordHash.ShouldNotBe(sent);
        BCrypt.Net.BCrypt.Verify(sent, user.passwordHash).ShouldBeTrue();
        await Email.Received(1).SendTempPasswordAsync(req.email, "Sami Hroub", Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Existing_email_is_rejected_and_nothing_is_sent()
    {
        var req = DoctorReq();
        await NewDepartments().RegisterDoctorAsync(C.Id, req);
        Email.ClearReceivedCalls();

        await ShouldFailWith("Auth.EmailExists", () => NewDepartments().RegisterDoctorAsync(C.Id, DoctorReq(req.email)));
        await Email.DidNotReceiveWithAnyArgs().SendTempPasswordAsync(default!, default!, default!, default!);
    }

    [Fact]
    public Task Unknown_department_is_rejected() =>
        ShouldFailWith("Department.NotFound", () => NewDepartments().RegisterDoctorAsync(C.Id, DoctorReq(departmentId: 999_999)));

    [Fact]
    public Task Unknown_center_is_rejected() =>
        ShouldFailWith("Center.NotFound", () => NewDepartments().RegisterDoctorAsync(999_999, DoctorReq()));

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task If_the_email_cannot_be_sent_no_half_account_is_left_behind()
    {
        Email.SendTempPasswordAsync(default!, default!, default!, default!).ReturnsForAnyArgs(Task.FromException(new InvalidOperationException("smtp down")));
        var req = DoctorReq();

        await Should.ThrowAsync<Exception>(() => NewDepartments().RegisterDoctorAsync(C.Id, req));

        (await UserByEmailAsync(req.email)).ShouldBeNull();
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Registering_a_doctor_is_audited()
    {
        var r = await NewDepartments().RegisterDoctorAsync(C.Id, DoctorReq());

        await using var db = Db();
        (await db.AuditLogs.AsNoTracking().AnyAsync(a => a.Action == "RegisterDoctor" && a.EntityId == r.UserId)).ShouldBeTrue();
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Two_requests_with_the_same_email_at_once_give_one_account_and_one_business_error()
    {
        var email = NewEmail();
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            try { await NewDepartments().RegisterDoctorAsync(C.Id, DoctorReq(email)); return (Exception?)null; }
            catch (Exception e) { return e; }
        }));

        results.Count(e => e == null).ShouldBe(1);
        results.Single(e => e != null).ShouldBeOfType<BusinessException>();
    }
}
