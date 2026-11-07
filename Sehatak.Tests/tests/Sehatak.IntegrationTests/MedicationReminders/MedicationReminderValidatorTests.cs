using Sehatak.Application.DTOs.MedicationReminderDto;
using Sehatak.Application.Validators.MedicationReminder;
using Shouldly;

namespace Sehatak.IntegrationTests.MedicationReminders;

/// <summary>The API runs this validator automatically (AddFluentValidationAutoValidation), so what it rejects never reaches the service.</summary>
public class MedicationReminderValidatorTests
{
    private readonly MedicationReminderRequestDtoValidator _v = new();

    private static MedicationReminderRequestDto Ok(Action<MedicationReminderRequestDto>? edit = null)
    {
        var r = new MedicationReminderRequestDto
        {
            MedicationName = "Panadol", ReminderTime = new TimeOnly(8, 0), IsDaily = false,
            SelectedDays = ReminderDays.Monday, DurationInDays = 5
        };
        edit?.Invoke(r);
        return r;
    }

    /// <summary>Rule `IsDaily.Null()` on a bool: a bool is never null, so EVERY request fails and no reminder can be added through the API.</summary>
    [Theory]
    [Trait("Category", "KnownBug")]
    [InlineData(false)]
    [InlineData(true)]
    public void A_normal_request_is_valid(bool daily)
        => _v.Validate(Ok(r => { r.IsDaily = daily; r.SelectedDays = ReminderDays.Monday; })).IsValid.ShouldBeTrue();

    [Theory]
    [Trait("Category", "KnownBug")]
    [InlineData(0)]
    [InlineData(-3)]
    public void Duration_must_be_at_least_one_day(int days)
        => _v.Validate(Ok(r => r.DurationInDays = days)).Errors.ShouldContain(e => e.PropertyName == nameof(MedicationReminderRequestDto.DurationInDays));

    [Fact]
    public void Duration_over_90_days_is_rejected()
        => _v.Validate(Ok(r => r.DurationInDays = 91)).Errors.ShouldContain(e => e.PropertyName == nameof(MedicationReminderRequestDto.DurationInDays));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Name_is_required(string name)
        => _v.Validate(Ok(r => r.MedicationName = name)).Errors.ShouldContain(e => e.PropertyName == nameof(MedicationReminderRequestDto.MedicationName));

    [Fact]
    public void Name_longer_than_200_is_rejected()
        => _v.Validate(Ok(r => r.MedicationName = new string('a', 201))).Errors.ShouldContain(e => e.PropertyName == nameof(MedicationReminderRequestDto.MedicationName));

    [Theory]
    [InlineData(0)]      // no day selected: the reminder would never fire
    [InlineData(128)]    // a bit that is not a day
    public void Selected_days_must_be_real_days(int bits)
        => _v.Validate(Ok(r => r.SelectedDays = (ReminderDays)bits)).Errors.ShouldContain(e => e.PropertyName == nameof(MedicationReminderRequestDto.SelectedDays));
}
