using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Patients;

[Collection(SqlServerCollection.Name)]
public class ViewProfileServiceTests(SqlServerFixture fx) : PatientsTestBase(fx)
{
    [Fact]
    public async Task A_patient_sees_the_account_and_the_medical_details()
    {
        await SetProfileImageAsync(Pat.UserId, "/uploads/profileImage/p.png");
        var u = await UserAsync(Pat.UserId);

        var r = await NewViewProfile().ViewPatientProfileAsync(C.Id, Pat.UserId);

        r.Id.ShouldBe(Pat.UserId);
        r.FirstName.ShouldBe(u.firstName);
        r.LastName.ShouldBe(u.lastName);
        r.Email.ShouldBe(u.email);
        r.PhoneNumber.ShouldBe(u.phoneNumber);
        r.City.ShouldBe("Hebron");
        r.Address.ShouldBe("addr");
        r.ProfileImage.ShouldBe("/uploads/profileImage/p.png");
        r.BloodType.ShouldBe(BloodType.O_Positive);
        r.Gender.ShouldBe(Gender.Male);
        r.DateOfBith.ShouldBe(new DateOnly(1990, 1, 1));
    }

    [Fact]
    public async Task A_doctor_sees_the_account_and_the_professional_details()
    {
        var u = await UserAsync(Doc.UserId);

        var r = await NewViewProfile().ViewDoctorProfiAsync(C.Id, Doc.UserId);

        r.Id.ShouldBe(Doc.UserId);
        r.FirstName.ShouldBe(u.firstName);
        r.Email.ShouldBe(u.email);
        r.Specialization.ShouldBe("GP");
        r.Bio.ShouldBe("bio");
        r.OnlineEnabled.ShouldBeFalse();
    }

    [Fact]
    public async Task A_staff_member_sees_the_account_without_medical_fields()
    {
        var rec = await C.AddReceptionistAsync();
        var u = await UserAsync(rec);

        var r = await NewViewProfile().ViewStaffProfileAsync(C.Id, rec);

        r.Id.ShouldBe(rec);
        r.FirstName.ShouldBe(u.firstName);
        r.Email.ShouldBe(u.email);
        r.city.ShouldBe("Hebron");
    }

    [Fact]
    public async Task A_deactivated_account_has_no_profile()
    {
        var gonePat = await C.AddPatientAsync(active: false);
        var goneDoc = await C.AddDoctorAsync(active: false);
        var goneRec = await C.AddReceptionistAsync(active: false);

        await ShouldFailWith("User.NotFound", () => NewViewProfile().ViewPatientProfileAsync(C.Id, gonePat.UserId));
        await ShouldFailWith("Doctor.NotFound", () => NewViewProfile().ViewDoctorProfiAsync(C.Id, goneDoc.UserId));
        await ShouldFailWith("User.NotFound", () => NewViewProfile().ViewStaffProfileAsync(C.Id, goneRec));
    }

    [Fact]
    public async Task An_unknown_user_has_no_profile()
    {
        await ShouldFailWith("User.NotFound", () => NewViewProfile().ViewPatientProfileAsync(C.Id, 999_999));
        await ShouldFailWith("Doctor.NotFound", () => NewViewProfile().ViewDoctorProfiAsync(C.Id, 999_999));
        await ShouldFailWith("User.NotFound", () => NewViewProfile().ViewStaffProfileAsync(C.Id, 999_999));
    }

    [Fact]
    public async Task An_unavailable_center_has_no_profiles()
    {
        var suspended = await Fx.Harness.CreateCenterAsync(CenterStatus.Suspended);

        await ShouldFailWith("Center.NotFound", () => NewViewProfile().ViewPatientProfileAsync(999_999, Pat.UserId));
        await ShouldFailWith("Center.NotFound", () => NewViewProfile().ViewDoctorProfiAsync(suspended.Id, Doc.UserId));
        await ShouldFailWith("Center.NotFound", () => NewViewProfile().ViewStaffProfileAsync(suspended.Id, Pat.UserId));
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Asking_for_the_wrong_kind_of_profile_is_a_clean_error_not_a_crash()
    {
        // a doctor id on the patient endpoint (or the reverse) dereferences a missing row -> NullReferenceException -> 500
        await ShouldFailWith("User.NotFound", () => NewViewProfile().ViewPatientProfileAsync(C.Id, Doc.UserId));
        await ShouldFailWith("Doctor.NotFound", () => NewViewProfile().ViewDoctorProfiAsync(C.Id, Pat.UserId));
    }
}
