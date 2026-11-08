using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace Sehatak.IntegrationTests.Conventions;

/// <summary>
/// The API answers errors with a CODE ("Expense.NotFound") and the ExceptionMiddleware turns it into text from Messages.en/ar.resx.
/// A code with no resx entry reaches the user as the raw code.
/// </summary>
public class ResourceMessagesTests
{
    private static readonly Regex CodeShape = new(@"^[A-Za-z]+\.[A-Za-z0-9_]+$");
    private static readonly Regex BusinessCode = new(@"new\s+BusinessException\(\s*""(?<code>[^""]+)""");
    private static readonly Regex ValidatorCode = new(@"\.WithMessage\(\s*""(?<code>[^""]+)""");

    [Fact]
    public void English_and_Arabic_have_exactly_the_same_keys()
    {
        var en = SourceTree.ReadResx("en").Keys.ToHashSet();
        var ar = SourceTree.ReadResx("ar").Keys.ToHashSet();

        en.Except(ar).OrderBy(x => x).ToList().ShouldBeEmpty("ناقصة بالعربي");
        ar.Except(en).OrderBy(x => x).ToList().ShouldBeEmpty("ناقصة بالإنجليزي");
    }

    [Fact]
    public void No_message_is_empty_and_no_arabic_message_is_a_copy_of_the_english_one()
    {
        var en = SourceTree.ReadResx("en");
        var ar = SourceTree.ReadResx("ar");

        en.Where(kv => string.IsNullOrWhiteSpace(kv.Value)).Select(kv => kv.Key).ShouldBeEmpty("إنجليزي فاضي");
        ar.Where(kv => string.IsNullOrWhiteSpace(kv.Value)).Select(kv => kv.Key).ShouldBeEmpty("عربي فاضي");
        ar.Where(kv => en.TryGetValue(kv.Key, out var e) && e == kv.Value).Select(kv => kv.Key).ShouldBeEmpty("العربي نسخة من الإنجليزي");
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public void Every_BusinessException_code_has_a_message()
    {
        var en = SourceTree.ReadResx("en");
        var missing = SourceTree.Find(BusinessCode)
            .Where(h => !en.ContainsKey(h.Match.Groups["code"].Value))
            .GroupBy(h => h.Match.Groups["code"].Value)
            .OrderBy(g => g.Key)
            .Select(g => $"{g.Key}   ({g.First().Where})")
            .ToList();

        missing.ShouldBeEmpty("أكواد بدون رسالة بالـ resx (المستخدم رح يشوف الكود نفسه):\n" + string.Join("\n", missing));
    }

    [Fact]
    public void Every_validator_message_code_has_a_message()
    {
        var en = SourceTree.ReadResx("en");
        var missing = SourceTree.Find(ValidatorCode)
            .Where(h => CodeShape.IsMatch(h.Match.Groups["code"].Value))
            .Where(h => !en.ContainsKey(h.Match.Groups["code"].Value))
            .Select(h => $"{h.Match.Groups["code"].Value}   ({h.Where})")
            .Distinct()
            .ToList();

        missing.ShouldBeEmpty(string.Join("\n", missing));
    }
}
