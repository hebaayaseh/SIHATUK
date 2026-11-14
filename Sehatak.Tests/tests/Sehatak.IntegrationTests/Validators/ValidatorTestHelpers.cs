using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Shouldly;

namespace Sehatak.IntegrationTests.Validators;

/// <summary>Small helpers so each validator test reads as "this value gives this error key on this property".</summary>
internal static class ValidatorTestHelpers
{
    private static string Describe(ValidationResult r) =>
        r.Errors.Count == 0 ? "(no errors)" : string.Join(", ", r.Errors.Select(e => $"{e.PropertyName}={e.ErrorMessage}"));

    public static void ShouldBeValid<T>(this IValidator<T> v, T model)
    {
        var r = v.Validate(model);
        r.IsValid.ShouldBeTrue("expected valid but got: " + Describe(r));
    }

    /// <summary>The property has an error whose message is the given resource key (other errors may exist).</summary>
    public static void ShouldFail<T>(this IValidator<T> v, T model, string property, string key)
    {
        var r = v.Validate(model);
        r.Errors.Any(e => e.PropertyName == property && e.ErrorMessage == key)
            .ShouldBeTrue($"expected {property} -> {key} but got: {Describe(r)}");
    }

    public static void ShouldNotFailOn<T>(this IValidator<T> v, T model, string property)
    {
        var r = v.Validate(model);
        r.Errors.Any(e => e.PropertyName == property)
            .ShouldBeFalse($"{property} should be accepted but got: {Describe(r)}");
    }

    /// <summary>Sets a public property by name (the DTOs use mixed casing, so tests name the property explicitly).</summary>
    public static T With<T>(this T model, string property, object? value)
    {
        var p = typeof(T).GetProperty(property) ?? throw new ArgumentException($"{typeof(T).Name} has no property {property}");
        p.SetValue(model, value);
        return model;
    }

    public static IFormFile FormFileOf(string fileName, string contentType, long length = 10) =>
        new FormFile(new MemoryStream(new byte[Math.Min(length, 16)]), 0, length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };

    public static string Long(int length) => new('a', length);
}
