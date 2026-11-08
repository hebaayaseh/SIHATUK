using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Sehatak.Application.DTOs.EditProfile.EditEmailOrPasswored;
using Sehatak.Application.DTOs.EditProfile.EditProfileActors;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Patients;

/// <summary>Staff / doctor profile edit, e-mail + password change, and the center's own settings.</summary>
[Collection(SqlServerCollection.Name)]
public class EditStaffServiceTests(SqlServerFixture fx) : PatientsTestBase(fx)
{
    private const string OldPassword = "Old@12345";
    private const string NewPassword = "New@12345";

    private async Task<Doctor> DoctorRowAsync()
    {
        await using var db = Db();
        return await db.Doctors.AsNoTracking().SingleAsync(d => d.Id == Doc.DoctorId);
    }

    // ---------- profile ----------

    [Fact]
    public async Task A_doctor_edit_changes_the_account_and_the_professional_fields()
    {
        var before = await UserAsync(Doc.UserId);

        var r = await NewEditStaff().EditSttafInformation(C.Id, Doc.UserId, new EditSttafInformationRequest
        {
            firstNmae = "Sami", phoneNumber = "0591112222", Specialization = "Cardiology", Bio = "new bio", OnlineEnabled = true
        });

        var u = await UserAsync(Doc.UserId);
        u.firstName.ShouldBe("Sami");
        u.phoneNumber.ShouldBe("0591112222");
        u.lastName.ShouldBe(before.lastName);
        u.address.ShouldBe(before.address);
        var d = await DoctorRowAsync();
        d.Specialization.ShouldBe("Cardiology");
        d.Bio.ShouldBe("new bio");
        d.OnlineEnabled.ShouldBeTrue();
        r.StaffId.ShouldBe(Doc.UserId);
        r.Specialization.ShouldBe("Cardiology");
        r.FullName.ShouldBe($"Sami {before.lastName}");
    }

    [Fact]
    public async Task A_doctor_edit_without_professional_fields_leaves_them_alone()
    {
        await NewEditStaff().EditSttafInformation(C.Id, Doc.UserId, new EditSttafInformationRequest { city = "Nablus" });

        var d = await DoctorRowAsync();
        d.Specialization.ShouldBe("GP");
        d.Bio.ShouldBe("bio");
        d.OnlineEnabled.ShouldBeFalse();
    }

    [Fact]
    public async Task A_receptionist_edit_ignores_the_doctor_fields()
    {
        var rec = await C.AddReceptionistAsync();

        var r = await NewEditStaff().EditSttafInformation(C.Id, rec,
            new EditSttafInformationRequest { city = "Nablus", Specialization = "Surgery", Bio = "x" });

        (await UserAsync(rec)).city.ShouldBe("Nablus");
        r.Specialization.ShouldBeNull();
        r.Bio.ShouldBeNull();
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task The_edit_response_returns_the_city()
    {
        var r = await NewEditStaff().EditSttafInformation(C.Id, Doc.UserId, new EditSttafInformationRequest { city = "Nablus" });

        r.City.ShouldBe("Nablus");   // the field exists but is never filled
    }

    [Fact]
    public async Task A_bad_picture_is_refused()
    {
        await ShouldFailWith("Validation.InvalidFileType", () => NewEditStaff().EditSttafInformation(C.Id, Doc.UserId,
            new EditSttafInformationRequest { profileImage = Image("a.gif") }));
        await ShouldFailWith("Validation.FileTooLarge", () => NewEditStaff().EditSttafInformation(C.Id, Doc.UserId,
            new EditSttafInformationRequest { profileImage = Image("a.jpg", 5 * 1024 * 1024 + 1) }));
    }

    [Fact]
    public async Task Edit_fails_for_a_deactivated_account_and_an_unavailable_center()
    {
        var gone = await C.AddReceptionistAsync(active: false);

        await ShouldFailWith("Auth.Forbidden", () => NewEditStaff().EditSttafInformation(C.Id, gone, new EditSttafInformationRequest()));
        await ShouldFailWith("Auth.Forbidden", () => NewEditStaff().EditSttafInformation(C.Id, 999_999, new EditSttafInformationRequest()));
        await ShouldFailWith("Center.NotFound", () => NewEditStaff().EditSttafInformation(999_999, Doc.UserId, new EditSttafInformationRequest()));
    }

    [Fact]
    public async Task A_doctor_account_without_a_doctor_row_is_a_clean_error()
    {
        int userId;
        await using (var db = Db())
        {
            var u = new User
            {
                firstName = "No", lastName = "Row", email = $"norow.{Guid.NewGuid():N}@test.local", passwordHash = "x",
                phoneNumber = "0591234500", address = "a", city = "c", role = userRole.Doctor, isActive = true
            };
            db.Users.Add(u);
            await db.SaveChangesAsync();
            userId = u.Id;
        }

        await ShouldFailWith("Doctor.NotFound", () => NewEditStaff().EditSttafInformation(C.Id, userId, new EditSttafInformationRequest()));
    }

    // ---------- e-mail ----------

    [Fact]
    public async Task The_email_change_needs_the_code_and_works_once()
    {
        await NewEditStaff().RequestEditEmail(C.Id, Doc.UserId, new EditEmailRequest { Email = "doc.new@test.local" });
        var row = await LastCodeAsync(Doc.UserId, "change-email");
        row.PendingValue.ShouldBe("doc.new@test.local");
        (await UserAsync(Doc.UserId)).email.ShouldNotBe("doc.new@test.local");

        var r = await NewEditStaff().ConfirmEditEmail(C.Id, Doc.UserId, new ConfirmEditEmailRequest { Code = row.Code });

        r.Email.ShouldBe("doc.new@test.local");
        await ShouldFailWith("Verfiy.Code", () => NewEditStaff().ConfirmEditEmail(C.Id, Doc.UserId, new ConfirmEditEmailRequest { Code = row.Code }));
    }

    [Fact]
    public async Task An_address_that_is_already_used_is_refused()
    {
        var other = await UserAsync(await C.AddReceptionistAsync());

        await ShouldFailWith("Auth.EmailExists", () => NewEditStaff().RequestEditEmail(C.Id, Doc.UserId, new EditEmailRequest { Email = other.email }));
        await Email.DidNotReceiveWithAnyArgs().SendOtpAsync(default!, default!, default!);
    }

    [Fact]
    public async Task Another_users_code_or_an_expired_code_is_refused()
    {
        var rec = await C.AddReceptionistAsync();
        await NewEditStaff().RequestEditEmail(C.Id, Doc.UserId, new EditEmailRequest { Email = "doc.new@test.local" });
        var row = await LastCodeAsync(Doc.UserId, "change-email");

        await ShouldFailWith("Verfiy.Code", () => NewEditStaff().ConfirmEditEmail(C.Id, rec, new ConfirmEditEmailRequest { Code = row.Code }));

        await ExpireCodeAsync(row.Id);
        await ShouldFailWith("Verfiy.Code", () => NewEditStaff().ConfirmEditEmail(C.Id, Doc.UserId, new ConfirmEditEmailRequest { Code = row.Code }));
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task If_someone_takes_the_address_before_the_code_is_confirmed_the_user_gets_a_clean_error()
    {
        await NewEditStaff().RequestEditEmail(C.Id, Doc.UserId, new EditEmailRequest { Email = "popular@test.local" });
        var code = (await LastCodeAsync(Doc.UserId, "change-email")).Code;
        var rec = await C.AddReceptionistAsync();
        await using (var db = Db())
        {
            var o = await db.Users.SingleAsync(u => u.Id == rec);
            o.email = "popular@test.local";
            await db.SaveChangesAsync();
        }

        await ShouldFailWith("Auth.EmailExists", () => NewEditStaff().ConfirmEditEmail(C.Id, Doc.UserId, new ConfirmEditEmailRequest { Code = code }));
    }

    // ---------- password ----------

    [Fact]
    public async Task The_password_change_works_end_to_end()
    {
        await SetPasswordAsync(Doc.UserId, OldPassword);
        await NewEditStaff().RequestEditPassword(C.Id, Doc.UserId, new EditPasswordRequest { PasswordHash = NewPassword, ConfirmPassword = NewPassword });
        var code = (await LastCodeAsync(Doc.UserId, "change-password")).Code;

        await NewEditStaff().ConfirmEditPassword(C.Id, Doc.UserId, new ConfirmEditPasswordRequest { Code = code });

        var hash = (await UserAsync(Doc.UserId)).passwordHash;
        BCrypt.Net.BCrypt.Verify(NewPassword, hash).ShouldBeTrue();
        BCrypt.Net.BCrypt.Verify(OldPassword, hash).ShouldBeFalse();
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task The_pending_new_password_is_never_stored_in_clear_text()
    {
        await SetPasswordAsync(Doc.UserId, OldPassword);

        await NewEditStaff().RequestEditPassword(C.Id, Doc.UserId, new EditPasswordRequest { PasswordHash = NewPassword, ConfirmPassword = NewPassword });

        var pending = (await LastCodeAsync(Doc.UserId, "change-password")).PendingValue;
        pending.ShouldNotBe(NewPassword);                              // today the plain password sits in the table
        BCrypt.Net.BCrypt.Verify(NewPassword, pending).ShouldBeTrue();
    }

    [Fact]
    public async Task A_mismatched_or_unchanged_password_is_refused()
    {
        await SetPasswordAsync(Doc.UserId, OldPassword);

        await ShouldFailWith("Validation.PasswordMismatch", () => NewEditStaff().RequestEditPassword(C.Id, Doc.UserId,
            new EditPasswordRequest { PasswordHash = NewPassword, ConfirmPassword = "Other@12345" }));
        await ShouldFailWith("Validation.SamePassword", () => NewEditStaff().RequestEditPassword(C.Id, Doc.UserId,
            new EditPasswordRequest { PasswordHash = OldPassword, ConfirmPassword = OldPassword }));

        (await CodeCountAsync(Doc.UserId)).ShouldBe(0);
    }

    [Fact]
    public async Task A_wrong_used_or_expired_password_code_changes_nothing()
    {
        await SetPasswordAsync(Doc.UserId, OldPassword);
        await NewEditStaff().RequestEditPassword(C.Id, Doc.UserId, new EditPasswordRequest { PasswordHash = NewPassword, ConfirmPassword = NewPassword });
        var row = await LastCodeAsync(Doc.UserId, "change-password");
        var wrong = row.Code == "000000" ? "111111" : "000000";

        await ShouldFailWith("Verfiy.Code", () => NewEditStaff().ConfirmEditPassword(C.Id, Doc.UserId, new ConfirmEditPasswordRequest { Code = wrong }));
        await ExpireCodeAsync(row.Id);
        await ShouldFailWith("Verfiy.Code", () => NewEditStaff().ConfirmEditPassword(C.Id, Doc.UserId, new ConfirmEditPasswordRequest { Code = row.Code }));

        BCrypt.Net.BCrypt.Verify(OldPassword, (await UserAsync(Doc.UserId)).passwordHash).ShouldBeTrue();
    }

    // ---------- center settings ----------

    [Fact]
    public async Task Center_edit_updates_the_fields_that_were_sent()
    {
        var r = await NewEditStaff().EditCenterInformation(C.Id, new EditCenterInformationRequest
        {
            Phone = "0592223333", Address = "New street", AdminWhatsappNumber = "0599998888",
            RequiresPrepayment = true, PrepaymentAmount = 50, RefundPolicyHours = 48, PartialRefundPercent = 30
        });

        var c = await CenterRowAsync();
        c.Phone.ShouldBe("0592223333");
        c.Address.ShouldBe("New street");
        c.AdminWhatsappNumber.ShouldBe("0599998888");
        c.RequiresPrepayment.ShouldBeTrue();
        c.PrepaymentAmount.ShouldBe(50m);
        c.RefundPolicyHours.ShouldBe(48);
        c.PartialRefundPercent.ShouldBe(30m);
        r.centerId.ShouldBe(C.Id);
        r.PrepaymentAmount.ShouldBe(50m);
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Changing_only_the_phone_does_not_reset_the_prepayment_settings()
    {
        await UpdateCenterRowAsync(c => { c.RequiresPrepayment = true; c.PrepaymentAmount = 50; c.RefundPolicyHours = 48; });

        // the DTO defaults (false / 0 / 24) are "provided" values as far as the service can tell
        await NewEditStaff().EditCenterInformation(C.Id, new EditCenterInformationRequest { Phone = "0592223333" });

        var c = await CenterRowAsync();
        c.Phone.ShouldBe("0592223333");
        c.RequiresPrepayment.ShouldBeTrue();
        c.PrepaymentAmount.ShouldBe(50m);
        c.RefundPolicyHours.ShouldBe(48);
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Editing_the_center_without_a_logo_keeps_the_existing_logo()
    {
        await UpdateCenterRowAsync(c => c.LogoUrl = "/uploads/receipts/logo.png");

        await NewEditStaff().EditCenterInformation(C.Id, new EditCenterInformationRequest { Phone = "0592223333" });

        (await CenterRowAsync()).LogoUrl.ShouldBe("/uploads/receipts/logo.png");   // today: wiped on every edit
    }

    [Fact]
    public async Task Center_edit_fails_for_an_unknown_or_suspended_center()
    {
        await ShouldFailWith("Center.NotFound", () => NewEditStaff().EditCenterInformation(999_999, new EditCenterInformationRequest()));

        var suspended = await Fx.Harness.CreateCenterAsync(CenterStatus.Suspended);
        await ShouldFailWith("Center.NotFound", () => NewEditStaff().EditCenterInformation(suspended.Id, new EditCenterInformationRequest()));
    }
}
