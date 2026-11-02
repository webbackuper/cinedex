using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Cinedex.App.Services;
using Cinedex.Core.Localization;

namespace Cinedex.Tests;

[CollectionDefinition(nameof(LocalizationTests), DisableParallelization = true)]
public sealed class LocalizationSerialGroup;

/// <summary>
/// Les 16 langues : dictionnaires cohérents avec le catalogue des textes, paramètres identiques, interface traduite.
/// Hors parallélisme : la langue choisie est globale et changerait les messages attendus par les autres tests.
/// </summary>
[Collection(nameof(LocalizationTests))]
public sealed partial class LocalizationTests
{
    private static readonly string Root = FindRoot();

    private static readonly List<string> Catalog =
        JsonSerializer.Deserialize<List<string>>(File.ReadAllText(Path.Combine(Root, "src", "Cinedex.Core", "Localization", "Lang", "catalog.json")))!;

    /// <summary>Textes identiques dans toutes les langues (nom de l'application, formats techniques).</summary>
    private static readonly HashSet<string> Untranslatable = ["Cinedex", "3D", "https://", "tt1234567"];

    public static TheoryData<string> Languages => [.. Tr.Languages.Select(l => l.Code).Where(c => c != Tr.SourceLanguage)];

    [Fact]
    public void Sixteen_languages_are_offered()
    {
        Assert.Equal(16, Tr.Languages.Count);
        Assert.Equal(16, Tr.Languages.Select(l => l.Code).Distinct().Count());
        Assert.Equal(["ar", "ur"], Tr.Languages.Where(l => l.RightToLeft).Select(l => l.Code));
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Each_dictionary_matches_the_catalog_and_keeps_the_parameters(string code)
    {
        var strings = Tr.Load(code);
        Assert.NotEmpty(strings);
        foreach (var (french, translated) in strings)
        {
            Assert.Contains(french, Catalog);
            Assert.Equal(Holes(french), Holes(translated));
        }

        // Les textes non traduits sont des noms propres ou techniques (DVD, Dolby, Netflix…) : 85 % au moins le sont.
        Assert.True(strings.Count >= Catalog.Count * 0.85, $"{code} : {strings.Count}/{Catalog.Count}");
    }

    [Fact]
    public void Every_text_written_in_the_windows_is_in_the_catalog()
    {
        var missing = Directory.EnumerateFiles(Path.Combine(Root, "src", "Cinedex.App", "Views"), "*.axaml")
            .SelectMany(f => XamlText().Matches(Regex.Replace(File.ReadAllText(f), "<!--.*?-->", string.Empty, RegexOptions.Singleline)))
            .Select(m => System.Net.WebUtility.HtmlDecode(m.Groups[2].Value))
            .Where(t => t.Any(char.IsLetter) && !Untranslatable.Contains(t) && !Catalog.Contains(t))
            .Distinct()
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void Interface_texts_are_translated_when_set()
    {
        try
        {
            Tr.Use("en");
            UiTranslator.Install(null);
            Assert.Equal("Cancel", new TextBlock { Text = "Annuler" }.Text);
            Assert.Equal("Cancel", new Button { Content = "Annuler" }.Content);
            Assert.Equal("Search… (Ctrl+F)", new TextBox { PlaceholderText = "Rechercher… (Ctrl+F)" }.PlaceholderText);
            Assert.Equal("3 title(s) in the catalog", Tr.F("{0} titre(s) dans le catalogue", 3));
            Assert.Equal("Texte sans traduction", Tr.T("Texte sans traduction"));

            Tr.Use("ar");
            Assert.True(Tr.IsRightToLeft);
        }
        finally
        {
            Tr.Use(Tr.SourceLanguage);
        }
    }

    private static List<string> Holes(string text) => [.. HoleRegex().Matches(text).Select(m => m.Value).Order(StringComparer.Ordinal)];

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Cinedex.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("Racine du dépôt introuvable.");
    }

    [GeneratedRegex(@"\{\d+(?::[^}]*)?\}")]
    private static partial Regex HoleRegex();

    [GeneratedRegex("""\b(Text|Content|Header|Title|PlaceholderText|Watermark|ToolTip\.Tip)="([^"{][^"]*)" """, RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex XamlText();
}
