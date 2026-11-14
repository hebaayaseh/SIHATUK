using FluentValidation;
using Sehatak.Application.Validators.Auth;
using Sehatak.Application.Validators.Common;
using Shouldly;

namespace Sehatak.IntegrationTests.Validators;

/// <summary>
/// One net over every validator in the Application assembly, so a new validator that forgets a message key
/// or throws on an empty request is caught without anyone writing a test for it first.
/// </summary>
public class AllValidatorsSmokeTests
{
    private static readonly Type[] ValidatorTypes = typeof(RegisterRequestDtoValidator).Assembly.GetTypes()
        .Where(t => t is { IsAbstract: false, IsClass: true } && ModelTypeOf(t) is not null)
        .ToArray();

    public static IEnumerable<object[]> All() => ValidatorTypes.Select(t => new object[] { t.Name });

    private static Type? ModelTypeOf(Type t)
    {
        for (var b = t.BaseType; b is not null; b = b.BaseType)
            if (b.IsGenericType && b.GetGenericTypeDefinition() == typeof(AbstractValidator<>))
                return b.GetGenericArguments()[0];
        return null;
    }

    private static Type Find(string name) => ValidatorTypes.Single(t => t.Name == name);

    [Fact]
    public void The_assembly_has_the_validators_we_expect() => ValidatorTypes.Length.ShouldBeGreaterThan(50);

    [Fact]
    public void Every_validator_class_name_is_unique() =>
        ValidatorTypes.Select(t => t.Name).Distinct().Count().ShouldBe(ValidatorTypes.Length);

    [Theory]
    [MemberData(nameof(All))]
    public void An_empty_request_is_answered_with_errors_not_an_exception(string validatorName)
    {
        var vt = Find(validatorName);
        var validator = (IValidator)Activator.CreateInstance(vt)!;
        var modelType = ModelTypeOf(vt)!;
        if (modelType.GetConstructor(Type.EmptyTypes) is null && !modelType.IsValueType)
            return;   // a model without a default constructor cannot arrive empty from model binding either

        var model = Activator.CreateInstance(modelType)!;

        Should.NotThrow(() => validator.Validate(new ValidationContext<object>(model)));
    }

    [Theory]
    [MemberData(nameof(All))]
    public void Every_error_message_is_a_resource_key_not_a_sentence(string validatorName)
    {
        var vt = Find(validatorName);
        var validator = (IValidator)Activator.CreateInstance(vt)!;
        var modelType = ModelTypeOf(vt)!;
        if (modelType.GetConstructor(Type.EmptyTypes) is null && !modelType.IsValueType) return;

        var result = validator.Validate(new ValidationContext<object>(Activator.CreateInstance(modelType)!));

        // FluentValidation's own defaults ("'Email' must not be empty.") contain spaces; our keys never do.
        // A sentence here means a rule was written without WithMessage(ValidationKeys.*).
        result.Errors.Where(e => e.ErrorMessage.Contains(' '))
            .Select(e => $"{e.PropertyName}: {e.ErrorMessage}")
            .ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(All))]
    public void Every_error_message_is_declared_in_ValidationKeys(string validatorName)
    {
        var known = typeof(ValidationKeys).GetFields()
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToHashSet();

        var vt = Find(validatorName);
        var validator = (IValidator)Activator.CreateInstance(vt)!;
        var modelType = ModelTypeOf(vt)!;
        if (modelType.GetConstructor(Type.EmptyTypes) is null && !modelType.IsValueType) return;

        var result = validator.Validate(new ValidationContext<object>(Activator.CreateInstance(modelType)!));

        result.Errors.Select(e => e.ErrorMessage).Where(m => !known.Contains(m)).Distinct().ShouldBeEmpty();
    }
}
