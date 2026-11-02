using System.Reflection;

namespace Cinedex.App.Services;

public enum HelpBlockKind
{
    Paragraph,
    Bullet,
    Subheading,
}

/// <summary>Un paragraphe, une puce ou un sous-titre ; le texte peut contenir du **gras**.</summary>
public sealed record HelpBlock(HelpBlockKind Kind, string Text)
{
    public bool IsParagraph => Kind == HelpBlockKind.Paragraph;

    public bool IsBullet => Kind == HelpBlockKind.Bullet;

    public bool IsSubheading => Kind == HelpBlockKind.Subheading;
}

/// <summary>Une rubrique de l'aide (titre « ## » du fichier) et son contenu.</summary>
public sealed record HelpSection(string Title, IReadOnlyList<HelpBlock> Blocks)
{
    /// <summary>Texte brut de la rubrique, pour la recherche.</summary>
    public string PlainText { get; } = Title + "\n" + string.Join("\n", Blocks.Select(b => b.Text.Replace("**", string.Empty, StringComparison.Ordinal)));

    public override string ToString() => Title;
}

/// <summary>
/// Aide de l'application : fichiers Help/help-&lt;langue&gt;.md embarqués (français, et anglais pour les autres langues).
/// Format volontairement simple : « # » titre, « ## » rubrique, « ### » sous-titre, « - » puce, **gras**.
/// La rubrique « Nouveautés » commence par la version en cours : elle est complétée à chaque nouvelle version.
/// </summary>
public sealed record HelpDocument(string Title, IReadOnlyList<HelpSection> Sections)
{
    public static HelpDocument Load(string languageCode)
    {
        var code = languageCode == "fr" ? "fr" : "en";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Cinedex.App.Help.help-{code}.md")
            ?? throw new InvalidOperationException($"Aide introuvable : {code}");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    public static HelpDocument Parse(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        var title = "Cinedex";
        var sections = new List<HelpSection>();
        string? current = null;
        var blocks = new List<HelpBlock>();

        void Flush()
        {
            if (current is not null)
            {
                sections.Add(new HelpSection(current, [.. blocks]));
            }

            blocks.Clear();
        }

        foreach (var raw in markdown.Split('\n'))
        {
            var line = raw.TrimEnd('\r').Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                blocks.Add(new HelpBlock(HelpBlockKind.Subheading, line[4..]));
            }
            else if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                Flush();
                current = line[3..];
            }
            else if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                title = line[2..];
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                blocks.Add(new HelpBlock(HelpBlockKind.Bullet, line[2..]));
            }
            else
            {
                blocks.Add(new HelpBlock(HelpBlockKind.Paragraph, line));
            }
        }

        Flush();
        return new HelpDocument(title, sections);
    }
}
