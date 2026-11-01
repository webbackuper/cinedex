using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Bobine.Media;

public enum NfoWriteResult
{
    Created,
    Updated,
    Unchanged,
    Skipped,
}

public sealed record NfoWriteOptions
{
    /// <summary>Remplacer les valeurs déjà présentes (sinon seuls les champs absents ou vides sont ajoutés).</summary>
    public bool OverwriteExisting { get; init; }

    /// <summary>Copie « .nfo.bak » du fichier d'origine avant la première modification (jamais écrasée ensuite).</summary>
    public bool Backup { get; init; } = true;

    public int MaxActors { get; init; } = 15;
}

/// <summary>
/// Crée ou complète un .nfo de film au format Kodi, compatible avec l'exemple fourni :
/// déclaration UTF-8 standalone, racine &lt;movie&gt;, uniqueid imdb et tmdb (tmdb par défaut).
/// Les éléments inconnus de Bobine sont conservés tels quels ; les listes (genres, acteurs...) sont
/// complétées sans suppression. L'écriture est atomique (fichier temporaire puis remplacement).
/// </summary>
public static class NfoWriter
{
    // Ordre des éléments d'un nouveau fichier, celui de Kodi.
    private static readonly string[] Order =
    [
        "title", "originaltitle", "sorttitle", "year", "ratings", "userrating", "plot", "outline", "tagline", "runtime",
        "mpaa", "uniqueid", "genre", "country", "set", "tag", "director", "credits", "premiered", "studio", "trailer", "actor",
    ];

    public static NfoWriteResult Write(string nfoPath, NfoInfo values, NfoWriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(values);
        options ??= new NfoWriteOptions();
        XDocument doc;
        string? original = null;
        var exists = File.Exists(nfoPath);
        if (exists)
        {
            var info = new FileInfo(nfoPath);
            if (info.Length > NfoReader.MaxBytes)
            {
                return NfoWriteResult.Skipped;
            }

            original = NfoReader.DecodeTolerant(File.ReadAllBytes(nfoPath));
            var parsed = NfoReader.Parse(original);
            if (parsed?.Root is null || parsed.Root.Name.LocalName != "movie")
            {
                // .nfo texte ou d'un autre type (série...) : on ne le remplace que si on peut le sauvegarder.
                if (!options.Backup)
                {
                    return NfoWriteResult.Skipped;
                }

                doc = NewDocument();
            }
            else
            {
                doc = parsed;
            }
        }
        else
        {
            doc = NewDocument();
        }

        var root = doc.Root!;
        var changed = false;
        void Scalar(string name, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var element = root.Element(name);
            if (element is null)
            {
                Insert(root, new XElement(name, value.Trim()));
                changed = true;
            }
            else if ((element.Value.Trim().Length == 0 || options.OverwriteExisting) && element.Value != value.Trim())
            {
                element.Value = value.Trim();
                changed = true;
            }
        }

        void List(string name, IEnumerable<string> items)
        {
            var present = new HashSet<string>(root.Elements(name).Select(e => e.Value.Trim()), StringComparer.OrdinalIgnoreCase);
            foreach (var item in items.Where(i => !string.IsNullOrWhiteSpace(i)).Select(i => i.Trim()))
            {
                if (present.Add(item))
                {
                    Insert(root, new XElement(name, item));
                    changed = true;
                }
            }
        }

        void UniqueId(string type, string? value, bool isDefault)
        {
            if (value is null)
            {
                return;
            }

            var element = root.Elements("uniqueid").FirstOrDefault(e => string.Equals((string?)e.Attribute("type"), type, StringComparison.OrdinalIgnoreCase));
            if (element is null)
            {
                element = new XElement("uniqueid", new XAttribute("type", type), value);
                if (isDefault && !root.Elements("uniqueid").Any(e => (string?)e.Attribute("default") == "true"))
                {
                    element.Add(new XAttribute("default", "true"));
                }

                Insert(root, element);
                changed = true;
            }
            else if ((element.Value.Trim().Length == 0 || options.OverwriteExisting) && element.Value.Trim() != value)
            {
                element.Value = value;
                changed = true;
            }
        }

        var inv = CultureInfo.InvariantCulture;
        Scalar("title", values.Title);
        Scalar("originaltitle", values.OriginalTitle);
        Scalar("sorttitle", values.SortTitle);
        Scalar("year", values.Year?.ToString(inv));
        if (values.Rating is { } rating && root.Element("ratings") is null && root.Element("rating") is null)
        {
            Insert(root, new XElement(
                "ratings",
                new XElement(
                    "rating",
                    new XAttribute("name", "themoviedb"),
                    new XAttribute("max", "10"),
                    new XAttribute("default", "true"),
                    new XElement("value", rating.ToString("0.0", inv)),
                    values.Votes is { } v ? new XElement("votes", v.ToString(inv)) : null)));
            changed = true;
        }

        Scalar("userrating", values.UserRating?.ToString("0.#", inv));
        Scalar("plot", values.Plot);
        Scalar("outline", values.Outline);
        Scalar("tagline", values.Tagline);
        Scalar("runtime", values.RuntimeMinutes?.ToString(inv));
        Scalar("mpaa", values.Certification);
        UniqueId("imdb", values.ImdbId, isDefault: false);
        UniqueId("tmdb", values.TmdbId?.ToString(inv), isDefault: true);
        List("genre", values.Genres);
        List("country", values.Countries);
        if (values.Set is { } set && root.Element("set") is null)
        {
            Insert(root, new XElement("set", new XElement("name", set)));
            changed = true;
        }

        List("tag", values.Tags);
        List("director", values.Directors);
        List("credits", values.Writers);
        Scalar("premiered", values.Premiered?.ToString("yyyy-MM-dd", inv));
        List("studio", values.Studios);
        Scalar("trailer", values.Trailer);

        var actorNames = new HashSet<string>(root.Elements("actor").Select(a => a.Element("name")?.Value.Trim() ?? string.Empty), StringComparer.OrdinalIgnoreCase);
        foreach (var actor in values.Actors.Take(options.MaxActors))
        {
            if (actorNames.Add(actor.Name))
            {
                Insert(root, new XElement(
                    "actor",
                    new XElement("name", actor.Name),
                    actor.Role is null ? null : new XElement("role", actor.Role),
                    actor.Order is null ? null : new XElement("order", actor.Order.Value.ToString(inv))));
                changed = true;
            }
        }

        if (!changed)
        {
            return NfoWriteResult.Unchanged;
        }

        if (exists && options.Backup)
        {
            var backup = nfoPath + ".bak";
            if (!File.Exists(backup))
            {
                File.Copy(nfoPath, backup);
            }
        }

        Save(doc, nfoPath);
        return exists ? NfoWriteResult.Updated : NfoWriteResult.Created;
    }

    private static XDocument NewDocument() => new(new XDeclaration("1.0", "UTF-8", "yes"), new XElement("movie"));

    /// <summary>Place un nouvel élément après le dernier élément qui le précède dans l'ordre Kodi.</summary>
    private static void Insert(XElement root, XElement element)
    {
        var rank = Array.IndexOf(Order, element.Name.LocalName);
        var after = root.Elements()
            .Where(e => Array.IndexOf(Order, e.Name.LocalName) is var r && r >= 0 && r <= rank)
            .LastOrDefault();
        if (after is null)
        {
            var before = root.Elements().FirstOrDefault(e => Array.IndexOf(Order, e.Name.LocalName) > rank);
            if (before is null)
            {
                root.Add(element);
            }
            else
            {
                before.AddBeforeSelf(element);
            }
        }
        else
        {
            after.AddAfterSelf(element);
        }
    }

    private static void Save(XDocument doc, string path)
    {
        // Indentation propre : on retire les blancs d'origine puis on réindente tout le document.
        foreach (var text in doc.DescendantNodes().OfType<XText>().Where(t => string.IsNullOrWhiteSpace(t.Value) && t.Parent?.HasElements == true).ToList())
        {
            text.Remove();
        }

        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
            IndentChars = "  ",
            NewLineChars = "\n",
            OmitXmlDeclaration = true,
        };
        var temp = path + ".tmp";
        using (var stream = File.Create(temp))
        {
            stream.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n"u8);
            using var writer = XmlWriter.Create(stream, settings);
            doc.Root!.WriteTo(writer);
        }

        File.Move(temp, path, overwrite: true);
    }
}
