using System.Reflection;
using System.Text.RegularExpressions;
using CoopKeep.App.Localization;

namespace CoopKeep.App.Tests;

/// <summary>
/// Metinler: arayüzde kullanılan her anahtar iki dilde de var, yer tutucular ({0}…) iki dilde aynı.
/// Eksik bir anahtar ekranda anahtarın kendisi olarak görünürdü ("mm.world.hint" gibi).
/// </summary>
[Collection("Loc")]
public class LocalizationTests
{
    private static readonly Regex XamlKey = new(@"L\[([\w.\-]+)\]", RegexOptions.Compiled);
    private static readonly Regex CodeKey = new(@"\bL\[""([\w.\-]+)""\]", RegexOptions.Compiled);
    private static readonly Regex Placeholder = new(@"\{(\d+)(?:[:,][^}]*)?\}", RegexOptions.Compiled);

    private static string In(string language, string key)
    {
        var loc = Loc.Current;
        var before = loc.Language;
        try
        {
            loc.Language = language;
            return loc[key];
        }
        finally
        {
            loc.Language = before;
        }
    }

    /// <summary>Sözlüklerdeki bütün anahtarlar (yansımayla: sözlükler gizli).</summary>
    private static IEnumerable<string> AllKeys() =>
        typeof(Loc).GetFields(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(Dictionary<string, string>))
            .SelectMany(f => ((Dictionary<string, string>)f.GetValue(null)!).Keys)
            .Distinct();

    public static TheoryData<string> UsedKeys()
    {
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(Repo.AppSource, "*.axaml", SearchOption.AllDirectories))
            foreach (Match m in XamlKey.Matches(File.ReadAllText(file))) keys.Add(m.Groups[1].Value);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Repo.AppSource, "ViewModels"), "*.cs"))
            foreach (Match m in CodeKey.Matches(File.ReadAllText(file))) keys.Add(m.Groups[1].Value);

        var data = new TheoryData<string>();
        foreach (var k in keys) data.Add(k);
        return data;
    }

    [Theory]
    [MemberData(nameof(UsedKeys))]
    public void Used_key_exists_in_both_languages(string key)
    {
        Assert.NotEqual(key, In("tr", key));
        Assert.NotEqual(key, In("en", key));
    }

    [Fact]
    public void Every_key_has_both_languages_with_the_same_placeholders()
    {
        var problems = new List<string>();
        foreach (var key in AllKeys())
        {
            var tr = In("tr", key);
            var en = In("en", key);
            if (tr == key || en == key)
            {
                problems.Add($"{key}: {(tr == key ? "TR" : "EN")} yok");
                continue;
            }

            var a = Placeholder.Matches(tr).Select(m => m.Groups[1].Value).ToHashSet();
            var b = Placeholder.Matches(en).Select(m => m.Groups[1].Value).ToHashSet();
            if (!a.SetEquals(b)) problems.Add($"{key}: TR {{{string.Join(",", a)}}} / EN {{{string.Join(",", b)}}}");
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void Help_wiki_is_the_name_of_the_encyclopedia_page()
    {
        Assert.Equal("Yardım wiki", In("tr", "nav.encyclopedia"));
        Assert.Equal("Help wiki", In("en", "nav.encyclopedia"));
    }
}
