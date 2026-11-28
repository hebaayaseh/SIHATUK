using System.Net;
using Microsoft.EntityFrameworkCore;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Api;

/// <summary>
/// A new center from the first anonymous request to its first booking, entirely over HTTP:
/// request -> payment -> super admin confirms + approves -> owner logs in -> sets the center up -> patient books.
/// Every approval creates a real tenant database (like production).
/// </summary>
[Collection(SqlServerCollection.Name)]
public class CenterOnboardingFlowTests(SqlServerFixture fx) : OnboardingFlowBase(fx)
{
    [Fact]
    public async Task A_new_center_goes_from_request_to_first_booking_over_http()
    {
        var super = await SuperAdminLoginAsync();
        var planId = await AddPlanAsync();
        var name = $"E2E Clinic {Guid.NewGuid():N}"[..24];
        var email = $"owner.{Guid.NewGuid():N}@test.local";

        // 1. an anonymous visitor asks for a center and pays
        var requestId = await NewRequestAsync(planId, name, email);
        (await RecordRequestPaymentAsync(requestId)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // the request is not a center yet: not listed, and the owner cannot log in anywhere
        (await SearchListsAsync(name)).ShouldBeFalse();

        // 2. the super admin sees the payment, confirms it and approves
        var pending = await OkJsonAsync(await GetAsync("/CenterRegistration/superAdmin-get-pending-payments", super));
        JsonContains(pending, name).ShouldBeTrue(pending.ToString());

        var paymentId = await PaymentIdOfRequestAsync(requestId);
        (await SendAsync(HttpMethod.Post, $"/CenterRegistration/superAdmin-confirm-payments/{paymentId}", super)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var approve = await OkJsonAsync(await SendAsync(HttpMethod.Post, $"/CenterRegistration/superAdmin-approve-requests/{requestId}", super));
        approve.GetProperty("success").GetBoolean().ShouldBeTrue();

        await using var shared = Fx.Harness.NewSharedDb();
        var center = await shared.MedicalCenters.AsNoTracking().SingleAsync(c => c.Name == name);
        center.CenterStatus.ShouldBe(CenterStatus.Active);
        var o = new Onboarded(center.Id, name, email, requestId, planId, super);

        var subs = await SubscriptionsAsync(center.Id);
        subs.Count.ShouldBe(1);
        subs[0].Status.ShouldBe(SubscriptionStatus.Active);
        subs[0].PlanId.ShouldBe(planId);

        // 3. the center is public now
        (await SearchListsAsync(name)).ShouldBeTrue();

        // 4. the owner logs in with the password chosen in the request form
        var owner = await OwnerLoginAsync(o);
        owner.ShouldNotBeNullOrWhiteSpace();
        (await PostAsync($"/Auth/staff-login/{center.Id}", new { Email = email, Password = "Wrong!Pass1" })).StatusCode.ShouldNotBe(HttpStatusCode.OK);

        // 5. the owner sets the center up
        var admin = await AdminTokenAsync(o);
        var deptId = await AddDepartmentAsync(o, admin);
        var doc = await AddDoctorViaAdminAsync(o, admin, deptId);
        var rec = await AddReceptionistViaAdminAsync(o, admin);
        await AddMondayHoursAsync(o, admin, doc.DoctorId);
        await AddPricesAsync(o, admin);

        var prices = await OkJsonAsync(await GetAsync($"/ServicePrice/medicalStaff-get-service-price/{center.Id}?type={(int)ServiceType.Appointment}", admin));
        JsonContains(prices, "Visit").ShouldBeTrue(prices.ToString());

        // the staff the owner added can log in once they have a known password
        await SetPasswordAsync(center.Id, doc.Email);
        await SetPasswordAsync(center.Id, rec.Email);
        (await StaffLoginAsync(center.Id, doc.Email)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await StaffLoginAsync(center.Id, rec.Email)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // 6. a patient registers at the new center and books the first slot
        var date = NextMonday();
        var patient = await RegisterPatientAtAsync(center.Id);
        var book = await BookAtAsync(center.Id, doc.DoctorId, patient.Token, date);
        var json = await OkJsonAsync(book);
        json.GetProperty("success").GetBoolean().ShouldBeTrue(json.ToString());

        await using var db = Fx.Harness.Tenant(center.Id);
        (await db.Appointments.CountAsync(a => a.patientId == patient.PatientId && a.appointmentDate == date)).ShouldBe(1);

        // the same slot is gone for the next patient
        // the same slot is gone for the next patient: 200 with success=false and the other free slots
        var other = await RegisterPatientAtAsync(center.Id);
        var taken = await OkJsonAsync(await BookAtAsync(center.Id, doc.DoctorId, other.Token, date));
        taken.GetProperty("success").GetBoolean().ShouldBeFalse(taken.ToString());
        JsonContains(taken, "09:30").ShouldBeTrue(taken.ToString());
        await using var db2 = Fx.Harness.Tenant(center.Id);
        (await db2.Appointments.CountAsync(a => a.patientId == other.PatientId && a.appointmentDate == date)).ShouldBe(0);
    }

    [Fact]
    public async Task Only_a_super_admin_can_confirm_or_approve()
    {
        var super = await SuperAdminLoginAsync();
        var planId = await AddPlanAsync();
        var requestId = await NewRequestAsync(planId, $"E2E Clinic {Guid.NewGuid():N}"[..24], $"o.{Guid.NewGuid():N}@test.local");
        await RecordRequestPaymentAsync(requestId);
        var paymentId = await PaymentIdOfRequestAsync(requestId);

        foreach (var role in new[] { "Admin", "Doctor", "Receptionist", "Patient" })
        {
            var t = Token(role, C.Id);
            (await SendAsync(HttpMethod.Post, $"/CenterRegistration/superAdmin-confirm-payments/{paymentId}", t)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, role);
            (await SendAsync(HttpMethod.Post, $"/CenterRegistration/superAdmin-approve-requests/{requestId}", t)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, role);
        }

        (await SendAsync(HttpMethod.Post, $"/CenterRegistration/superAdmin-approve-requests/{requestId}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await GetAsync("/CenterRegistration/superAdmin-get-requests")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // and the request is still Pending: nothing happened
        var req = await OkJsonAsync(await GetAsync($"/CenterRegistration/superAdmin-get-request/{requestId}", super));
        JsonContains(req, "Pending").ShouldBeTrue(req.ToString());
    }

    [Fact]
    public async Task A_request_without_a_confirmed_payment_cannot_be_approved()
    {
        var super = await SuperAdminLoginAsync();
        var name = $"E2E Clinic {Guid.NewGuid():N}"[..24];
        var requestId = await NewRequestAsync(await AddPlanAsync(), name, $"o.{Guid.NewGuid():N}@test.local");

        (await SendAsync(HttpMethod.Post, $"/CenterRegistration/superAdmin-approve-requests/{requestId}", super)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // paid but not confirmed yet: still refused
        await RecordRequestPaymentAsync(requestId);
        (await SendAsync(HttpMethod.Post, $"/CenterRegistration/superAdmin-approve-requests/{requestId}", super)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        await using var shared = Fx.Harness.NewSharedDb();
        (await shared.MedicalCenters.AnyAsync(c => c.Name == name)).ShouldBeFalse("no center may exist before the approval");
    }

    [Fact]
    public async Task A_rejected_request_never_becomes_a_center_and_cannot_be_approved_later()
    {
        var super = await SuperAdminLoginAsync();
        var name = $"E2E Clinic {Guid.NewGuid():N}"[..24];
        var requestId = await NewRequestAsync(await AddPlanAsync(), name, $"o.{Guid.NewGuid():N}@test.local");
        await RecordRequestPaymentAsync(requestId);
        await SendAsync(HttpMethod.Post, $"/CenterRegistration/superAdmin-confirm-payments/{await PaymentIdOfRequestAsync(requestId)}", super);

        var reject = await OkJsonAsync(await SendAsync(HttpMethod.Post, $"/CenterRegistration/superAdmin-reject-requests/{requestId}", super, "Documents are not valid"));
        reject.GetProperty("success").GetBoolean().ShouldBeTrue();

        (await SendAsync(HttpMethod.Post, $"/CenterRegistration/superAdmin-approve-requests/{requestId}", super)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await SendAsync(HttpMethod.Post, $"/CenterRegistration/superAdmin-reject-requests/{requestId}", super, "again")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        await using var shared = Fx.Harness.NewSharedDb();
        (await shared.MedicalCenters.AnyAsync(c => c.Name == name)).ShouldBeFalse();
        (await SearchListsAsync(name)).ShouldBeFalse();
    }

    [Fact]
    public async Task The_same_center_name_cannot_be_requested_twice_while_one_is_pending()
    {
        var planId = await AddPlanAsync();
        var name = $"E2E Clinic {Guid.NewGuid():N}"[..24];
        await NewRequestAsync(planId, name, $"a.{Guid.NewGuid():N}@test.local");

        (await RequestCenterAsync(planId, name, $"b.{Guid.NewGuid():N}@test.local")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_bad_request_form_is_refused_before_anything_is_stored()
    {
        var planId = await AddPlanAsync();
        var name = $"E2E Clinic {Guid.NewGuid():N}"[..24];

        (await RequestCenterAsync(planId, name, "not-an-email")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await RequestCenterAsync(planId, name, $"x.{Guid.NewGuid():N}@test.local", password: "123")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await RequestCenterAsync(planId: 99_999_999, name, $"y.{Guid.NewGuid():N}@test.local")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        await using var shared = Fx.Harness.NewSharedDb();
        (await shared.centerRegistrationRequests.AnyAsync(r => r.CenterName == name)).ShouldBeFalse();
    }

    [Fact]
    public async Task One_owner_cannot_touch_another_center()
    {
        var super = await SuperAdminLoginAsync();
        var a = await OnboardAsync(super);
        var b = await OnboardAsync(super);
        var adminA = await AdminTokenAsync(a);

        (await GetAsync($"/Center/admin-get-my-center/{b.CenterId}", adminA)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await GetAsync($"/Center/admin-get-my-center/{a.CenterId}", adminA)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var form = new MultipartFormDataContent { { new StringContent("Intruder dept"), "departmentName" }, { new StringContent("x"), "departmentDescription" } };
        var req = new HttpRequestMessage(HttpMethod.Post, $"/Department/admin-add-department/{b.CenterId}") { Content = form };
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminA);
        (await Http.SendAsync(req)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        await using var db = Fx.Harness.Tenant(b.CenterId);
        (await db.Departments.AnyAsync(d => d.Name == "Intruder dept")).ShouldBeFalse();
    }

    [Fact]
    public async Task A_deactivated_staff_member_cannot_log_in_until_the_owner_reactivates_them()
    {
        var o = await OnboardAsync();
        var admin = await AdminTokenAsync(o);
        var rec = await AddReceptionistViaAdminAsync(o, admin);
        await SetPasswordAsync(o.CenterId, rec.Email);
        (await StaffLoginAsync(o.CenterId, rec.Email)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await SendAsync(HttpMethod.Put, $"/Staff/admin-remove-staff-from-center/{o.CenterId}", admin, new { userId = rec.UserId })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await StaffLoginAsync(o.CenterId, rec.Email)).StatusCode.ShouldNotBe(HttpStatusCode.OK);

        (await SendAsync(HttpMethod.Put, $"/Staff/admin-active-staff-from-center/{o.CenterId}", admin, new { userId = rec.UserId })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await StaffLoginAsync(o.CenterId, rec.Email)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Staff_roles_that_belong_elsewhere_cannot_be_added_as_staff()
    {
        var o = await OnboardAsync();
        var admin = await AdminTokenAsync(o);

        foreach (var role in new[] { "Doctor", "Patient", "SuperAdmin" })
        {
            var form = new MultipartFormDataContent
            {
                { new StringContent(role), "userRole" }, { new StringContent("A"), "FirstName" }, { new StringContent("B"), "LastName" },
                { new StringContent($"{role}.{Guid.NewGuid():N}@test.local"), "email" }, { new StringContent("0597777777"), "phoneNumber" },
                { new StringContent("St"), "address" }, { new StringContent("Hebron"), "city" },
            };
            var req = new HttpRequestMessage(HttpMethod.Post, $"/Staff/admin-add-staff/{o.CenterId}") { Content = form };
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", admin);
            (await Http.SendAsync(req)).StatusCode.ShouldBe(HttpStatusCode.BadRequest, role);
        }
    }

    [Fact]
    public async Task Only_the_owner_manages_departments_staff_and_prices()
    {
        var o = await OnboardAsync();
        var admin = await AdminTokenAsync(o);
        var (_, doctorId) = await SetUpBookableAsync(o);

        foreach (var role in new[] { "Doctor", "Receptionist", "Patient", "LabTechnician" })
        {
            var t = Token(role, o.CenterId, 1);
            (await SendAsync(HttpMethod.Post, $"/ServicePrice/admin-add-service-price/{o.CenterId}", t,
                new { Type = (int)ServiceType.Appointment, Items = new[] { new { ServiceName = "Hack", Price = 1m } } })).StatusCode.ShouldBe(HttpStatusCode.Forbidden, role);
            (await SendAsync(HttpMethod.Post, $"/DoctorDailyHours/admin-add-doctor-hours/{o.CenterId}/{doctorId}", t,
                new { DayOfWeek = 2, StartTime = "09:00:00", EndTime = "11:00:00", SlotDurationMinutes = 30 })).StatusCode.ShouldBe(HttpStatusCode.Forbidden, role);
            (await SendAsync(HttpMethod.Put, $"/Staff/admin-remove-staff-from-center/{o.CenterId}", t, new { userId = 1 })).StatusCode.ShouldBe(HttpStatusCode.Forbidden, role);
        }

        // the owner is allowed to see the same thing
        (await GetAsync($"/DoctorDailyHours/admin-get-doctor-hours/{o.CenterId}/{doctorId}", admin)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
