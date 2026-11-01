using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Bobine.Media;

public sealed record NfoActor(string Name, string? Role, int? Order);

/// <summary>
/// Contenu d'un .nfo de film au format Kodi (utilisé aussi par Jellyfin, Emby, tinyMediaManager).
/// Tous les champs sont facultatifs ; seuls ceux présents dans le fichier sont renseignés.
/// </summary>
public sealed record NfoInfo
{
    public string? Title { get; init; }

    public string? OriginalTitle { get; init; }

    public string? SortTitle { get; init; }

    public int? Year { get; init; }

    public string? ImdbId { get; init; }

    public long? TmdbId { get; init; }

    public string? Plot { get; init; }

    public string? Outline { get; init; }

    public string? Tagline { get; init; }

    public int? RuntimeMinutes { get; init; }

    public string? Certification { get; init; }

    public double? Rating { get; init; }

    public int? Votes { get; init; }

    public double? UserRating { get; init; }

    public DateOnly? Premiered { get; init; }

    public string? Set { get; init; }

    public string? Trailer { get; init; }

    public IReadOnlyList<string> Genres { get; init; } = [];

    public IReadOnlyList<string> Countries { get; init; } = [];

    public IReadOnlyList<string> Studios { get; init; } = [];

    public IReadOnlyList<string> Tags { get; init; } = [];

    public IReadOnlyList<string> Directors { get; init; } = [];

    public IReadOnlyList<string> Writers { get; init; } = [];

    public IReadOnlyList<NfoActor> Actors { get; init; } = [];

    public int? PlayCount { get; init; }

    public bool? Watched { get; init; }
}

/// <summary>
/// Lit et écrit les .nfo. Un .nfo est une donnée non fiable : XML lu sans DTD ni entités externes,
/// taille bornée. Les fichiers déclarés UTF-8 mais encodés en Latin-1 / Windows-1252 (fréquent :
/// « 37°2 ») sont relus dans ce second encodage plutôt que rejetés.
/// </summary>
public static partial class NfoReader
{
    public const int MaxBytes = 1024 * 1024;

    private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Cherche « film.nfo » puis « movie.nfo » à côté du fichier vidéo.</summary>
    public static string? FindFor(string videoPath)
    {
        var dir = Path.GetDirectoryName(videoPath)!;
        var sameName = NfoPathFor(videoPath);
        if (File.Exists(sameName))
        {
            return sameName;
        }

        var movie = Path.Combine(dir, "movie.nfo");
        return File.Exists(movie) ? movie : null;
    }

    /// <summary>Emplacement du .nfo associé à un fichier vidéo : même nom, extension .nfo.</summary>
    public static string NfoPathFor(string videoPath) =>
        Path.Combine(Path.GetDirectoryName(videoPath)!, Path.GetFileNameWithoutExtension(videoPath) + ".nfo");

    public static NfoInfo? Read(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length > MaxBytes)
        {
            return null;
        }

        var text = DecodeTolerant(File.ReadAllBytes(path));
        return text.TrimStart().StartsWith('<') ? ReadXml(text) ?? ReadText(text) : ReadText(text);
    }

    /// <summary>UTF-8 strict (BOM retiré), sinon Windows-1252, qui couvre Latin-1 et les guillemets « typographiques ».</summary>
    internal static string DecodeTolerant(byte[] bytes)
    {
        var span = bytes.AsSpan();
        if (span.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            span = span[3..];
        }

        try
        {
            return StrictUtf8.GetString(span);
        }
        catch (DecoderFallbackException)
        {
            return CodePagesEncodingProviderHolder.Windows1252.GetString(span);
        }
    }

    internal static NfoInfo? ReadXml(string xml)
    {
        var doc = Parse(xml);
        var root = doc?.Root;
        if (root is null)
        {
            return null;
        }

        string? Text(string name) => root.Element(name)?.Value.Trim() is { Length: > 0 } v ? v : null;
        IReadOnlyList<string> All(string name) => root.Elements(name)
            .SelectMany(e => e.Value.Split(" / ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(v => v.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        string? UniqueId(string type) => root.Elements("uniqueid")
            .FirstOrDefault(e => string.Equals((string?)e.Attribute("type"), type, StringComparison.OrdinalIgnoreCase))?.Value.Trim();

        var imdbRaw = UniqueId("imdb") ?? Text("imdbid") ?? Text("imdb_id") ?? Text("id");
        var tmdbRaw = UniqueId("tmdb") ?? Text("tmdbid");

        // Notes : <ratings><rating name="themoviedb" default="true"><value>7.9</value><votes>..</votes></rating></ratings> ou <rating>7.9</rating>.
        var rating = root.Element("ratings")?.Elements("rating")
            .OrderByDescending(r => (string?)r.Attribute("default") == "true")
            .FirstOrDefault();
        var ratingValue = ParseDouble(rating?.Element("value")?.Value) ?? ParseDouble(Text("rating"));
        var votes = ParseInt(rating?.Element("votes")?.Value) ?? ParseInt(Text("votes"));

        var premiered = Text("premiered") ?? Text("releasedate") ?? Text("aired");
        var premieredDate = premiered is { Length: >= 10 } && DateOnly.TryParseExact(premiered[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : (DateOnly?)null;
        var year = ParseInt(Text("year")) ?? premieredDate?.Year;

        var set = root.Element("set") is { } s ? (s.Element("name")?.Value ?? s.Value).Trim() : null;
        var certification = Text("mpaa") ?? Text("certification");

        return new NfoInfo
        {
            Title = Text("title"),
            OriginalTitle = Text("originaltitle"),
            SortTitle = Text("sorttitle"),
            Year = year is >= 1870 and <= 2100 ? year : null,
            ImdbId = imdbRaw is not null && ImdbPattern().Match(imdbRaw) is { Success: true } im ? im.Value : null,
            TmdbId = long.TryParse(tmdbRaw, NumberStyles.None, CultureInfo.InvariantCulture, out var t) && t > 0 ? t : null,
            Plot = Text("plot"),
            Outline = Text("outline"),
            Tagline = Text("tagline"),
            RuntimeMinutes = ParseInt(Text("runtime")) is > 0 and var rt ? rt : null,
            Certification = certification,
            Rating = ratingValue is >= 0 and <= 10 ? ratingValue : null,
            Votes = votes,
            UserRating = ParseDouble(Text("userrating")) is >= 0 and <= 10 and var ur ? ur : null,
            Premiered = premieredDate,
            Set = string.IsNullOrWhiteSpace(set) ? null : set,
            Trailer = Text("trailer") is { } tr && Uri.TryCreate(tr, UriKind.Absolute, out var tu) && tu.Scheme is "https" or "http" ? tu.AbsoluteUri : null,
            Genres = All("genre"),
            Countries = All("country"),
            Studios = All("studio"),
            Tags = All("tag"),
            Directors = All("director"),
            Writers = All("credits"),
            Actors = root.Elements("actor")
                .Select(a => (Name: a.Element("name")?.Value.Trim(), Role: a.Element("role")?.Value.Trim(), Order: ParseInt(a.Element("order")?.Value)))
                .Where(a => !string.IsNullOrEmpty(a.Name))
                .Select(a => new NfoActor(a.Name!, string.IsNullOrEmpty(a.Role) ? null : a.Role, a.Order))
                .ToList(),
            PlayCount = ParseInt(Text("playcount")),
            Watched = Text("watched") is { } w ? w.Equals("true", StringComparison.OrdinalIgnoreCase) : null,
        };
    }

    internal static NfoInfo? ReadText(string text)
    {
        var imdb = ImdbPattern().Match(text);
        var tmdb = TmdbUrlPattern().Match(text);
        if (!imdb.Success && !tmdb.Success)
        {
            return null;
        }

        return new NfoInfo
        {
            ImdbId = imdb.Success ? imdb.Value : null,
            TmdbId = tmdb.Success ? long.Parse(tmdb.Groups["id"].Value, CultureInfo.InvariantCulture) : null,
        };
    }

    /// <summary>XML sûr : pas de DTD, pas de résolution externe, taille bornée. Null si invalide.</summary>
    internal static XDocument? Parse(string xml)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxBytes,
            IgnoreComments = false,
        };
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), settings);
            return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static int? ParseInt(string? value) =>
        int.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static double? ParseDouble(string? value) =>
        double.TryParse(value?.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

    [GeneratedRegex(@"tt\d{7,10}")]
    private static partial Regex ImdbPattern();

    [GeneratedRegex(@"themoviedb\.org/movie/(?<id>\d{1,10})")]
    private static partial Regex TmdbUrlPattern();
}

/// <summary>Accès à l'encodage Windows-1252 (fournisseur de pages de codes chargé une fois).</summary>
internal static class CodePagesEncodingProviderHolder
{
    public static readonly Encoding Windows1252 = Create();

    private static Encoding Create()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252);
    }
}
