using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sehatak.Domain.Entities;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Api;

/// <summary>Helpers shared by the end-to-end flow suites. Everything goes through the real HTTP pipeline.</summary>
public abstract class FlowTestBase(SqlServerFixture fx) : ApiTestBase(fx)
{
    protected record FlowPatient(int UserId, int PatientId, string Email, string Token);

    protected string DateStr => Date.ToString("yyyy-MM-dd");

    protected Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string? token = null, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        if (body != null) req.Content = JsonContent.Create(body);
        if (token != null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return Http.SendAsync(req);
    }

    protected async Task<JsonElement> OkJsonAsync(HttpResponseMessage r)
    {
        var text = await r.Content.ReadAsStringAsync();
        r.StatusCode.ShouldBe(HttpStatusCode.OK, text);
        return JsonDocument.Parse(text).RootElement;
    }

    /// <summary>Register (multipart) -> read the e-mail code from the DB -> verify -> login.</summary>
    protected async Task<FlowPatient> RegisterVerifyLoginAsync(string first = "Rana")
    {
        var email = $"e2e.{Guid.NewGuid():N}@test.local";
        var form = new MultipartFormDataContent
        {
            { new StringContent(first), "firstName" },
            { new StringContent("Sami"), "lastName" },
            { new StringContent(email), "email" },
            { new StringContent($"059{Random.Shared.Next(1_000_000, 9_999_999)}"), "phoneNumber" },
            { new StringContent(Pwd), "password" },
            { new StringContent("Main street"), "address" },
            { new StringContent("Hebron"), "city" },
            { new StringContent("Female"), "gender" },
            { new StringContent("A_Positive"), "bloodType" },
            { new StringContent("1995-03-04"), "DateOfBith" },
        };

        var reg = await Http.PostAsync($"/Auth/patient-register-patient/{C.Id}", form);
        reg.StatusCode.ShouldBe(HttpStatusCode.OK, await reg.Content.ReadAsStringAsync());
        var userId = (await JsonAsync(reg)).GetProperty("userId").GetInt32();

        await using var db = Db();
        var code = await db.EmailVerificationCodes.Where(c => c.UserId == userId).Select(c => c.Code).SingleAsync();

        var verify = await PostAsync($"/Auth/patient-verify-code/{C.Id}", new { userId, code });
        verify.StatusCode.ShouldBe(HttpStatusCode.OK, await verify.Content.ReadAsStringAsync());

        var login = await PostAsync($"/Auth/patient-login-patient/{C.Id}", new { email, password = Pwd });
        var token = (await OkJsonAsync(login)).GetProperty("accessToken").GetString()!;

        var patientId = await db.Patients.Where(p => p.userId == userId).Select(p => p.patientId).SingleAsync();
        return new FlowPatient(userId, patientId, email, token);
    }

    protected Task<HttpResponseMessage> BookRaw(string token, string slot, int? subPatientId = null, string? date = null, int? doctorId = null) =>
        PostAsync($"/Appointment/patient-book/{C.Id}/{doctorId ?? Doc.DoctorId}",
            new { timeSlot = slot, dateOnly = date ?? DateStr, subPatientId }, token);

    /// <summary>Books and asserts success; returns the new appointment id.</summary>
    protected async Task<int> BookAsync(FlowPatient p, string slot = "09:00:00", int? subPatientId = null)
    {
        var res = await BookRaw(p.Token, slot, subPatientId);
        var json = await OkJsonAsync(res);
        json.GetProperty("success").GetBoolean().ShouldBeTrue(json.ToString());

        await using var db = Db();
        var target = subPatientId ?? p.PatientId;
        return await db.Appointments
            .Where(a => a.patientId == target && a.appointmentDate == Date && a.appointmentStatus != AppointmentStatus.Cancelled)
            .Select(a => a.Id).SingleAsync();
    }

    protected async Task<List<string>> FreeSlotsAsync()
    {
        var res = await GetAsync($"/Appointment/patient-get-available-doctor-slot/{C.Id}/{Doc.DoctorId}?date={DateStr}");
        var json = await OkJsonAsync(res);
        return json.GetProperty("dateAvailable").EnumerateArray().Select(e => e.GetString()![..5]).ToList();
    }

    protected async Task AddVisitPriceAsync()
    {
        await using var db = Db();
        db.ServicePrices.Add(new ServicePrice { ServiceName = "Visit", Price = 50m, Type = ServiceType.Appointment });
        await db.SaveChangesAsync();
    }

    protected string DoctorToken => Token("Doctor", C.Id, Doc.UserId);
    protected string AdminToken => Token("Admin", C.Id, C.AdminUserId);
    protected string ReceptionistToken(int userId) => Token("Receptionist", C.Id, userId);

    /// <summary>True if any string value anywhere in the JSON contains the text.</summary>
    protected static bool JsonContains(JsonElement e, string text) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString()!.Contains(text, StringComparison.OrdinalIgnoreCase),
        JsonValueKind.Object => e.EnumerateObject().Any(p => JsonContains(p.Value, text)),
        JsonValueKind.Array => e.EnumerateArray().Any(x => JsonContains(x, text)),
        _ => false
    };
}
