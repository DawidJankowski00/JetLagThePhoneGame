using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace TransitGames.Core.Tests;

/// <summary>
/// Every resource file of the app (Polish, neutral) has an English twin with the same keys, no empty texts and the
/// same format placeholders — a missing "{1}" would throw at runtime in one language only.
/// </summary>
public partial class TranslationTests
{
    public static TheoryData<string> ResourceFiles() => new(NeutralResourceFiles());

    /// <summary>Neutral (Polish) resource files, relative to the repository root.</summary>
    private static IEnumerable<string> NeutralResourceFiles() =>
        Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "src"), "*.resx", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && !Path.GetFileNameWithoutExtension(f).Contains('.'))
            .Order()
            .Select(f => Path.GetRelativePath(RepositoryRoot(), f));

    [Fact]
    public void All_resource_files_are_found()
    {
        var names = NeutralResourceFiles().Select(f => Path.GetFileName(f)).ToList();
        foreach (var expected in (string[])["CoreStrings.resx", "UiStrings.resx", "HideAndSeekStrings.resx", "TagStrings.resx"])
        {
            Assert.Contains(expected, names);
        }
    }

    [Theory]
    [MemberData(nameof(ResourceFiles))]
    public void English_matches_Polish(string relativePath)
    {
        var polishPath = Path.Combine(RepositoryRoot(), relativePath);
        var englishPath = Path.ChangeExtension(polishPath, ".en.resx");
        Assert.True(File.Exists(englishPath), $"Missing {Path.GetFileName(englishPath)}");

        var polish = Read(polishPath);
        var english = Read(englishPath);

        Assert.Empty(polish.Keys.Except(english.Keys));
        Assert.Empty(english.Keys.Except(polish.Keys));
        foreach (var (key, text) in polish)
        {
            Assert.False(string.IsNullOrWhiteSpace(text), $"{key}: empty Polish text");
            Assert.False(string.IsNullOrWhiteSpace(english[key]), $"{key}: empty English text");
            Assert.True(
                Placeholders(text).SetEquals(Placeholders(english[key])),
                $"{key}: placeholders differ: '{text}' vs '{english[key]}'");
        }
    }

    private static Dictionary<string, string> Read(string path) =>
        XDocument.Load(path).Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? string.Empty);

    private static HashSet<string> Placeholders(string text) =>
        [.. PlaceholderPattern().Matches(text).Select(m => m.Groups[1].Value)];

    [GeneratedRegex(@"\{(\d+)(?:[:,][^}]*)?\}")]
    private static partial Regex PlaceholderPattern();

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "TransitGames.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
