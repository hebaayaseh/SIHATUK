using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.CenterRegistration;

[Collection(SqlServerCollection.Name)]
public class CenterRequestTests(SqlServerFixture fx) : RegistrationTestBase(fx)
{
    private static IFormFile Fake(string fileName, long length)
    {
        var f = Substitute.For<IFormFile>();
        f.FileName.Returns(fileName);
        f.Length.Returns(length);
        return f;
    }

    [Fact]
    public async Task A_valid_request_is_saved_Pending_with_a_hashed_password_and_the_applicant_is_emailed()
    {
        var plan = await AddPlanAsync();
        var dto = Req(plan);

        var r = await NewService().CenterRequestAsync(dto);

        r.Status.ShouldBe(CenterRegistrationStatus.Pending);
        r.CenterName.ShouldBe(dto.CenterName);
        var row = await RequestRowAsync(r.Id);
        row.PlanId.ShouldBe(plan);
        row.AdminEmail.ShouldBe(dto.AdminEmail);
        row.PasswordHash.ShouldNotBe(Pwd);
        BCrypt.Net.BCrypt.Verify(Pwd, row.PasswordHash).ShouldBeTrue();
        row.RequestedAt.ShouldBe(DateTime.UtcNow, TimeSpan.FromMinutes(2));
        await Email.Received(1).SendCustomMessageAsync(dto.AdminEmail, Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task A_name_already_used_by_a_center_is_rejected()
    {
        var plan = await AddPlanAsync();
        var existing = await Fx.Harness.CreateCenterAsync();
        await using (var shared = Fx.Harness.NewSharedDb())
        {
            var c = await shared.MedicalCenters.SingleAsync(x => x.Id == existing.Id);
            c.Name = UniqueName("Taken");
            await shared.SaveChangesAsync();
            await ShouldFailWith("Center.UrlExists", () => NewService().CenterRequestAsync(Req(plan, c.Name)));
        }
    }

    [Fact]
    public async Task Unknown_plan_is_rejected()
    {
        await ShouldFailWith("Subscription.PlanNotFound", () => NewService().CenterRequestAsync(Req(999_999)));
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_retired_plan_cannot_be_chosen()
    {
        var retired = await AddPlanAsync(active: false);
        await ShouldFailWith("Subscription.PlanNotFound", () => NewService().CenterRequestAsync(Req(retired)));
    }

    [Fact]
    public async Task A_second_pending_request_with_the_same_email_is_rejected_but_after_a_rejection_the_email_can_apply_again()
    {
        var plan = await AddPlanAsync();
        var email = NewEmail();
        var (first, _) = await NewRequestAsync(plan, email: email);

        await ShouldFailWith("CenterRegistration.AlreadyPending", () => NewService().CenterRequestAsync(Req(plan, email: email)));

        await NewService().RejectAsync(first, await AddSuperAdminAsync(), "incomplete");
        (await NewService().CenterRequestAsync(Req(plan, email: email))).Id.ShouldNotBe(first);
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Two_pending_requests_cannot_ask_for_the_same_center_name()
    {
        var plan = await AddPlanAsync();
        var name = UniqueName();
        await NewRequestAsync(plan, name);

        await ShouldFailWith("Center.UrlExists", () => NewService().CenterRequestAsync(Req(plan, name)));
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Prepayment_and_refund_settings_chosen_at_registration_are_kept()
    {
        var plan = await AddPlanAsync();
        var (id, _) = await NewRequestAsync(plan, tweak: d =>
        {
            d.RequiresPrepayment = true; d.PrepaymentAmount = 50m; d.RefundPolicyHours = 12; d.PartialRefundPercent = 30m;
        });

        var row = await RequestRowAsync(id);
        row.RequiresPrepayment.ShouldBeTrue();
        row.PrepaymentAmount.ShouldBe(50m);
        row.RefundPolicyHours.ShouldBe(12);
        row.PartialRefundPercent.ShouldBe(30m);
    }

    [Fact]
    public async Task Logo_must_be_jpg_png_or_pdf_and_at_most_5MB()
    {
        var plan = await AddPlanAsync();
        await ShouldFailWith("Validation.InvalidFileType", () => NewService().CenterRequestAsync(Req(plan).WithLogo(Fake("x.exe", 10))));
        await ShouldFailWith("Validation.FileTooLarge", () => NewService().CenterRequestAsync(Req(plan).WithLogo(Fake("x.png", 5 * 1024 * 1024 + 1))));
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_failing_email_does_not_turn_a_saved_request_into_an_error()
    {
        var plan = await AddPlanAsync();
        Email.SendCustomMessageAsync(default!, default!, default!).ReturnsForAnyArgs(Task.FromException(new InvalidOperationException("smtp down")));
        var dto = Req(plan);

        await Should.NotThrowAsync(() => NewService().CenterRequestAsync(dto));
    }
}

internal static class RegistrationDtoExtensions
{
    /// <summary>Tiny helper so a test can set the logo inline.</summary>
    public static Sehatak.Application.DTOs.CreateCenterRequestDto.CenterRegistrationRequestDto WithLogo(
        this Sehatak.Application.DTOs.CreateCenterRequestDto.CenterRegistrationRequestDto dto, IFormFile logo)
    {
        dto.LogoUrl = logo;
        return dto;
    }
}
