using System.Text.RegularExpressions;

namespace Sehatak.IntegrationTests.Conventions;

/// <summary>Finds the repository (walks up from the test output folder) so tests can read the production source and resx files.</summary>
internal static class SourceTree
{
    private static readonly string[] ProductionProjects =
        { "Sehatak.Domain", "Sehatak.Application", "Sehatak.Infrastructure", "Sehatak.API" };

    public static string Root { get; } = FindRoot();

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Sehatak.API")) && Directory.Exists(Path.Combine(dir.FullName, "Sehatak.Infrastructure")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not find the repository root from " + AppContext.BaseDirectory);
    }

    /// <summary>Every production .cs file (no Migrations, obj, bin).</summary>
    public static IEnumerable<string> ProductionFiles() =>
        ProductionProjects
            .Select(p => Path.Combine(Root, p))
            .Where(Directory.Exists)
            .SelectMany(p => Directory.EnumerateFiles(p, "*.cs", SearchOption.AllDirectories))
            .Where(f =>
            {
                var rel = f.Replace('\\', '/');
                return !rel.Contains("/Migrations/") && !rel.Contains("/obj/") && !rel.Contains("/bin/");
            });

    /// <summary>Every line of production code that matches <paramref name="pattern"/>, as "relative/path.cs:line: text".</summary>
    public static List<(string Where, Match Match, string Line)> Find(Regex pattern, Func<string, bool>? skipFile = null)
    {
        var hits = new List<(string, Match, string)>();
        foreach (var file in ProductionFiles())
        {
            if (skipFile != null && skipFile(file)) continue;
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("//")) continue;
                foreach (Match m in pattern.Matches(lines[i]))
                    hits.Add(($"{Path.GetRelativePath(Root, file).Replace('\\', '/')}:{i + 1}", m, lines[i].Trim()));
            }
        }
        return hits;
    }

    public static Dictionary<string, string> ReadResx(string language)
    {
        var path = Path.Combine(Root, "Sehatak.API", "Resources", $"Messages.{language}.resx");
        var doc = System.Xml.Linq.XDocument.Load(path);
        return doc.Descendants("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? "");
    }
}
