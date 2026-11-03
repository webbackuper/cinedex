using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Cinedex.Tests;

/// <summary>
/// Garde-fou du système de design : les vues n'utilisent que les jetons de Styles/Tokens.axaml et la palette
/// d'App.axaml, et respectent les règles verrouillées du Taste Skill (échelle de 4 px, cinq tailles de texte,
/// pas d'ombre au repos, pas de dégradé hors voile sur photo, pas de petites capitales espacées).
/// </summary>
public sealed partial class DesignTokensTests
{
    private static readonly string AppDirectory = FindAppDirectory();

    private static string FindAppDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Cinedex.App");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("src/Cinedex.App introuvable depuis le dossier des tests.");
    }

    /// <summary>Vues et contrôles : tout le XAML sauf le dictionnaire de jetons lui-même.</summary>
    public static TheoryData<string> Views()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.EnumerateFiles(AppDirectory, "*.axaml", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                                 && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                                 && !f.EndsWith("Tokens.axaml", StringComparison.Ordinal))
                     .Order(StringComparer.Ordinal))
        {
            data.Add(Path.GetRelativePath(AppDirectory, file));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Views))]
    public void Views_use_design_tokens_instead_of_raw_values(string file)
    {
        var text = File.ReadAllText(Path.Combine(AppDirectory, file));
        var problems = new List<string>();

        // Espacements, marges, rayons, tailles de texte, bordures : un jeton, ou 0.
        foreach (Match m in RawDesignValue().Matches(text))
        {
            problems.Add($"{m.Groups["name"].Value}=\"{m.Groups["value"].Value}\"");
        }

        foreach (Match m in RawSetterValue().Matches(text))
        {
            problems.Add($"Setter {m.Groups["name"].Value} = {m.Groups["value"].Value}");
        }

        // Couleurs : la palette d'App.axaml est la seule exception (thèmes clair et sombre).
        if (!file.Equals("App.axaml", StringComparison.Ordinal))
        {
            problems.AddRange(HexColor().Matches(text).Select(m => $"couleur {m.Value}"));
        }

        Assert.True(problems.Count == 0, $"{file} : valeurs en dur → jetons de Styles/Tokens.axaml : {string.Join(", ", problems)}");
    }

    [Theory]
    [MemberData(nameof(Views))]
    public void Views_follow_the_locked_rules_of_the_taste_skill(string file)
    {
        var text = File.ReadAllText(Path.Combine(AppDirectory, file));

        // Pas d'ombre sur ce qui est au repos : seulement l'affiche de l'économiseur d'écran, qui flotte.
        if (!file.EndsWith("ScreenSaverWindow.axaml", StringComparison.Ordinal))
        {
            Assert.DoesNotContain("BoxShadow", text, StringComparison.Ordinal);
        }

        // Pas de dégradé, sauf le voile neutre sous un texte posé sur une photo (jetons Scrim*).
        foreach (Match m in GradientStop().Matches(text))
        {
            Assert.StartsWith("{StaticResource Scrim", m.Groups["color"].Value, StringComparison.Ordinal);
        }

        // Pas de petites capitales espacées, pas de libellé tout en capitales (les sigles comme EAN ou HTTPS restent permis).
        Assert.DoesNotContain("LetterSpacing", text, StringComparison.Ordinal);
        foreach (var label in XDocument.Parse(text).Descendants().Attributes("Text").Select(a => a.Value).Where(v => !v.StartsWith('{')))
        {
            var letters = label.Where(char.IsLetter).ToList();
            var shouted = ShoutedWord().IsMatch(label) && letters.Count(char.IsUpper) * 5 > letters.Count * 4;
            Assert.False(shouted, $"{file} : libellé en capitales « {label} »");
        }
    }

    [Fact]
    public void Tokens_stay_on_the_scale()
    {
        var tokens = XDocument.Load(Path.Combine(AppDirectory, "Styles", "Tokens.axaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        int[] scale = [0, 1, 4, 8, 12, 16, 24, 32, 48, 64];

        // Espacements et marges sur la base de 4 px (1 px : la bordure des surfaces de travail).
        foreach (var thickness in tokens.Root!.Elements().Where(e => e.Name.LocalName == "Thickness"))
        {
            foreach (var part in thickness.Value.Split(','))
            {
                Assert.Contains(int.Parse(part.Trim(), System.Globalization.CultureInfo.InvariantCulture), scale);
            }
        }

        // Cinq tailles de texte dans l'application (plus les deux de l'économiseur d'écran, lu de loin).
        var fonts = tokens.Root.Elements().Where(e => e.Name.LocalName == "Double" && e.Attribute(x + "Key")!.Value.StartsWith("Font", StringComparison.Ordinal)).ToList();
        Assert.Equal(5, fonts.Count(f => !f.Attribute(x + "Key")!.Value.StartsWith("FontDisplay", StringComparison.Ordinal)));
    }

    [GeneratedRegex(@"\b(?<name>Margin|Padding|Spacing|ColumnSpacing|RowSpacing|CornerRadius|BorderThickness|FontSize)=""(?<value>(?!0"")[0-9][0-9.,\s]*)""")]
    private static partial Regex RawDesignValue();

    [GeneratedRegex(@"\b\p{Lu}{5,}\b")]
    private static partial Regex ShoutedWord();

    [GeneratedRegex(@"Property=""(?<name>Margin|Padding|CornerRadius|BorderThickness|FontSize)"" Value=""(?<value>(?!0"")[0-9][0-9.,\s]*)""")]
    private static partial Regex RawSetterValue();

    [GeneratedRegex(@"""#[0-9A-Fa-f]{6,8}""")]
    private static partial Regex HexColor();

    [GeneratedRegex(@"<GradientStop[^>]*Color=""(?<color>[^""]*)""")]
    private static partial Regex GradientStop();
}
