using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;

namespace Bobine.Media;

public sealed record NfoInfo(string? Title, int? Year, string? ImdbId, long? TmdbId);

/// <summary>
/// Lit les fichiers .nfo (format XML de Kodi, ou simple texte contenant un lien IMDb/TMDb).
/// XML lu sans DTD ni entités externes, taille bornée : un .nfo est une donnée non fiable.
/// </summary>
public static partial class NfoReader
{
    public const int MaxBytes = 1024 * 1024;

    /// <summary>Cherche « film.nfo » puis « movie.nfo » à côté du fichier vidéo.</summary>
    public static string? FindFor(string videoPath)
    {
        var dir = Path.GetDirectoryName(videoPath)!;
        var sameName = Path.Combine(dir, Path.GetFileNameWithoutExtension(videoPath) + ".nfo");
        if (File.Exists(sameName))
        {
            return sameName;
        }

        var movie = Path.Combine(dir, "movie.nfo");
        return File.Exists(movie) ? movie : null;
    }

    public static NfoInfo? Read(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length > MaxBytes)
        {
            return null;
        }

        var text = File.ReadAllText(path);
        return text.TrimStart().StartsWith('<') ? ReadXml(text) ?? ReadText(text) : ReadText(text);
    }

    internal static NfoInfo? ReadXml(string xml)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxBytes,
            IgnoreComments = true,
        };
        try
        {
            var doc = new XmlDocument { XmlResolver = null };
            using (var reader = XmlReader.Create(new StringReader(xml), settings))
            {
                doc.Load(reader);
            }

            var root = doc.DocumentElement;
            if (root is null)
            {
                return null;
            }

            string? Text(string xpath) => root.SelectSingleNode(xpath)?.InnerText?.Trim() is { Length: > 0 } v ? v : null;
            var imdb = Text("uniqueid[@type='imdb']") ?? Text("imdbid") ?? Text("id");
            var tmdb = Text("uniqueid[@type='tmdb']") ?? Text("tmdbid");
            var year = Text("year") ?? Text("premiered")?[..4];
            return new NfoInfo(
                Text("title"),
                int.TryParse(year, NumberStyles.None, CultureInfo.InvariantCulture, out var y) ? y : null,
                imdb is not null && ImdbPattern().IsMatch(imdb) ? ImdbPattern().Match(imdb).Value : null,
                long.TryParse(tmdb, NumberStyles.None, CultureInfo.InvariantCulture, out var t) ? t : null);
        }
        catch (XmlException)
        {
            return null;
        }
    }

    internal static NfoInfo? ReadText(string text)
    {
        var imdb = ImdbPattern().Match(text);
        var tmdb = TmdbUrlPattern().Match(text);
        if (!imdb.Success && !tmdb.Success)
        {
            return null;
        }

        return new NfoInfo(
            null,
            null,
            imdb.Success ? imdb.Value : null,
            tmdb.Success ? long.Parse(tmdb.Groups["id"].Value, CultureInfo.InvariantCulture) : null);
    }

    /// <summary>Écrit un .nfo minimal (format Kodi) ; n'écrase jamais un fichier existant.</summary>
    public static bool WriteIfMissing(string videoPath, string title, int? year, string? imdbId, long? tmdbId)
    {
        var target = Path.Combine(Path.GetDirectoryName(videoPath)!, Path.GetFileNameWithoutExtension(videoPath) + ".nfo");
        if (File.Exists(target))
        {
            return false;
        }

        var settings = new XmlWriterSettings { Indent = true, Encoding = new System.Text.UTF8Encoding(false) };
        using var writer = XmlWriter.Create(target, settings);
        writer.WriteStartElement("movie");
        writer.WriteElementString("title", title);
        if (year is not null)
        {
            writer.WriteElementString("year", year.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (imdbId is not null)
        {
            writer.WriteStartElement("uniqueid");
            writer.WriteAttributeString("type", "imdb");
            writer.WriteString(imdbId);
            writer.WriteEndElement();
        }

        if (tmdbId is not null)
        {
            writer.WriteStartElement("uniqueid");
            writer.WriteAttributeString("type", "tmdb");
            writer.WriteString(tmdbId.Value.ToString(CultureInfo.InvariantCulture));
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        return true;
    }

    [GeneratedRegex(@"tt\d{7,10}")]
    private static partial Regex ImdbPattern();

    [GeneratedRegex(@"themoviedb\.org/movie/(?<id>\d{1,10})")]
    private static partial Regex TmdbUrlPattern();
}
