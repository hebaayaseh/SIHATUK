using Sehatak.Application.DTOs.EmergencyDto;
using Sehatak.Application.Common;
using Sehatak.Domain.Enums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Emergency;

/// <summary>The receptionist registers a walk-in emergency and assigns the general doctor who is on shift today.</summary>
[Collection(SqlServerCollection.Name)]
public class RegisterEmergencyTests(SqlServerFixture fx) : EmergencyTestBase(fx)
{
    private EmergencyRequestDto Req(int? doctor = null, string name = "Mahmoud Ali", bool insured = false, decimal amount = 100m) =>
        new() { PatientName = name, DoctorUserId = doctor ?? GenDoc, IsInsurance = insured, AmountPaid = amount };

    private Task<EmergencyResponseDto> Register(EmergencyRequestDto req, int? userId = null) =>
        NewEmergencyService().RegisterPatientEmergencyAsync(C.Id, userId ?? Rec, req);

    [Fact]
    public async Task Registers_the_case_and_audits_it()
    {
        var r = await Register(Req());

        r.PatientName.ShouldBe("Mahmoud Ali");
        r.DoctorUserId.ShouldBe(GenDoc);
        r.ReceptionistId.ShouldBe(Rec);
        r.AmountPaid.ShouldBe(100m);
        r.IsInsurance.ShouldBeFalse();

        var e = await LoadAsync(r.Id);
        e.PatientName.ShouldBe("Mahmoud Ali");
        e.DoctorUserId.ShouldBe(GenDoc);
        e.ReceptionistId.ShouldBe(Rec);
        e.AmountPaid.ShouldBe(100m);
        ShouldBeNowUtc(e.CreatedAt);
        ShouldBeNowUtc(e.UpdatedAt);
        (await CountAuditAsync("RegisterEmergency", r.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task An_insured_case_with_no_amount_is_saved()
    {
        var r = await Register(Req(insured: true, amount: 0m));
        var e = await LoadAsync(r.Id);
        e.IsInsurance.ShouldBeTrue();
        e.AmountPaid.ShouldBe(0m);
    }

    [Fact]
    public async Task Two_cases_for_the_same_doctor_are_both_saved()
    {
        await Register(Req(name: "Patient One"));
        await Register(Req(name: "Patient Two"));
        (await CountCasesAsync()).ShouldBe(2);
    }

    /// <summary>The response is built with a second DateTime.UtcNow, so it differs by a few ms from what was stored.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Response_timestamps_match_the_stored_ones()
    {
        var r = await Register(Req());
        var e = await LoadAsync(r.Id);
        r.CreatedAt.ShouldBe(e.CreatedAt);
        r.UpdatedAt.ShouldBe(e.UpdatedAt);
    }

    [Theory]
    [InlineData(AttendanceStatus.Present)]
    [InlineData(AttendanceStatus.Late)]
    [InlineData(AttendanceStatus.EarlyOut)]
    public async Task A_doctor_who_attended_in_any_form_can_be_assigned(AttendanceStatus status)
    {
        var doc = await AddGeneralDoctorAsync();
        await AddAttendanceAsync(doc, status);
        (await Register(Req(doc))).DoctorUserId.ShouldBe(doc);
    }

    [Theory]
    [InlineData(AttendanceStatus.Absent)]
    [InlineData(AttendanceStatus.OnLeave)]
    public async Task DoctorShift_NotFound_when_absent_or_on_leave(AttendanceStatus status)
    {
        var doc = await AddGeneralDoctorAsync();
        await AddAttendanceAsync(doc, status);
        await ShouldFailWith("DoctorShift.NotFound", () => Register(Req(doc)));
        (await CountCasesAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task DoctorShift_NotFound_when_the_doctor_has_no_shift_today()
    {
        var doc = await AddGeneralDoctorAsync();
        await ShouldFailWith("DoctorShift.NotFound", () => Register(Req(doc)));
    }

    [Fact]
    public async Task DoctorShift_NotFound_when_the_shift_is_on_another_day()
    {
        var doc = await AddGeneralDoctorAsync();
        await AddAttendanceAsync(doc, AttendanceStatus.Present, ClinicClock.Today.AddDays(-1));
        await ShouldFailWith("DoctorShift.NotFound", () => Register(Req(doc)));
    }

    [Fact]
    public async Task Doctor_NotFound_for_an_unknown_doctor()
    {
        await ShouldFailWith("Doctor.NotFound", () => Register(Req(999_999)));
        (await CountCasesAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Doctor_NotFound_for_an_inactive_general_doctor()
    {
        var doc = await AddGeneralDoctorAsync(active: false);
        await AddAttendanceAsync(doc);
        await ShouldFailWith("Doctor.NotFound", () => Register(Req(doc)));
    }

    [Fact]
    public async Task Doctor_NotFound_when_the_user_is_a_specialist_not_a_general_doctor()
    {
        await AddAttendanceAsync(Doc.UserId);
        await ShouldFailWith("Doctor.NotFound", () => Register(Req(Doc.UserId)));
    }

    [Fact]
    public Task Receptionist_NotFound_for_an_unknown_user() =>
        ShouldFailWith("Receptionist.NotFound", () => Register(Req(), 999_999));

    [Fact]
    public async Task Receptionist_NotFound_for_an_inactive_receptionist()
    {
        var inactive = await C.AddReceptionistAsync(active: false);
        await ShouldFailWith("Receptionist.NotFound", () => Register(Req(), inactive));
    }

    [Fact]
    public Task Receptionist_NotFound_when_the_user_is_not_a_receptionist() =>
        ShouldFailWith("Receptionist.NotFound", () => Register(Req(), Doc.UserId));

    [Fact]
    public Task Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => NewEmergencyService().RegisterPatientEmergencyAsync(999_999, Rec, Req()));
}
