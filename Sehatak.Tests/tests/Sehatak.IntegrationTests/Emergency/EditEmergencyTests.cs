using Sehatak.Application.DTOs.EmergencyDto;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Emergency;

/// <summary>Receptionist corrects a registered case: only the fields that are sent are changed.</summary>
[Collection(SqlServerCollection.Name)]
public class EditEmergencyTests(SqlServerFixture fx) : EmergencyTestBase(fx)
{
    private Task<EmergencyResponseDto> Edit(UpdateEmergencyRequestDto req, int? userId = null) =>
        NewEmergencyService().EditPatientEmergencyAsync(C.Id, userId ?? Rec, req);

    [Fact]
    public async Task Changes_only_the_name_when_only_the_name_is_sent()
    {
        var id = await SeedEmergencyAsync("Old Name", insured: false, amount: 40m, createdUtc: DateTime.UtcNow.AddHours(-2));

        var r = await Edit(new() { EmergencyId = id, PatientName = "New Name" });

        r.PatientName.ShouldBe("New Name");
        var e = await LoadAsync(id);
        e.PatientName.ShouldBe("New Name");
        e.AmountPaid.ShouldBe(40m);
        e.IsInsurance.ShouldBeFalse();
        e.DoctorUserId.ShouldBe(GenDoc);
        e.ReceptionistId.ShouldBe(Rec);
        ShouldBeNowUtc(e.UpdatedAt);
        e.CreatedAt.ShouldBeLessThan(e.UpdatedAt.AddHours(-1));
        (await CountAuditAsync("EditEmergency", id)).ShouldBe(1);
    }

    [Fact]
    public async Task Changes_only_the_amount_when_only_the_amount_is_sent()
    {
        var id = await SeedEmergencyAsync("Ali", amount: 40m);
        var r = await Edit(new() { EmergencyId = id, AmountPaid = 75.5m });
        r.AmountPaid.ShouldBe(75.5m);
        var e = await LoadAsync(id);
        e.AmountPaid.ShouldBe(75.5m);
        e.PatientName.ShouldBe("Ali");
    }

    [Fact]
    public async Task Amount_can_be_set_to_zero()
    {
        var id = await SeedEmergencyAsync(amount: 40m);
        await Edit(new() { EmergencyId = id, AmountPaid = 0m });
        (await LoadAsync(id)).AmountPaid.ShouldBe(0m);
    }

    [Fact]
    public async Task Insurance_can_be_switched_off()
    {
        var id = await SeedEmergencyAsync(insured: true, amount: 0m);
        await Edit(new() { EmergencyId = id, IsInsurance = false, AmountPaid = 30m });
        var e = await LoadAsync(id);
        e.IsInsurance.ShouldBeFalse();
        e.AmountPaid.ShouldBe(30m);
    }

    [Fact]
    public async Task All_three_fields_can_change_together()
    {
        var id = await SeedEmergencyAsync("A", insured: false, amount: 10m);
        await Edit(new() { EmergencyId = id, PatientName = "B", IsInsurance = true, AmountPaid = 0m });
        var e = await LoadAsync(id);
        e.PatientName.ShouldBe("B");
        e.IsInsurance.ShouldBeTrue();
        e.AmountPaid.ShouldBe(0m);
    }


    /// <summary>Register checks the receptionist is active and has the role; Edit ignores userId completely.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Receptionist_NotFound_for_an_unknown_user()
    {
        var id = await SeedEmergencyAsync();
        await ShouldFailWith("Receptionist.NotFound", () => Edit(new() { EmergencyId = id, PatientName = "X Y" }, 999_999));
        (await LoadAsync(id)).PatientName.ShouldBe("Ali");
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Receptionist_NotFound_for_an_inactive_receptionist()
    {
        var id = await SeedEmergencyAsync();
        var inactive = await C.AddReceptionistAsync(active: false);
        await ShouldFailWith("Receptionist.NotFound", () => Edit(new() { EmergencyId = id, PatientName = "X Y" }, inactive));
    }

    [Fact]
    public Task Emergency_NotFound_for_an_unknown_case() =>
        ShouldFailWith("Emergency.NotFound", () => Edit(new() { EmergencyId = 999_999, PatientName = "X Y" }));

    [Fact]
    public Task Center_NotFound() =>
        ShouldFailWith("Center.NotFound",
            () => NewEmergencyService().EditPatientEmergencyAsync(999_999, Rec, new() { EmergencyId = 1, PatientName = "X Y" }));
}
