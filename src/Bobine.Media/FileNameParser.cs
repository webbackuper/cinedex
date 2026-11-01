using System.Globalization;
using System.Text.RegularExpressions;

namespace Bobine.Media;

/// <summary>Ce qu'on peut déduire d'un nom de fichier vidéo.</summary>
public sealed record ParsedName(
    string Title,
    int? Year,
    int? Season,
    int? Episode,
    int? Part,
    string? Resolution,
    string? ImdbId)
{
    public bool IsEpisode => Season is not null && Episode is not null;
}

/// <summary>
/// Analyse les noms de fichiers usuels : « Heat.1995.1080p.BluRay.x264-GRP.mkv », « Heat (1995).mkv »,
/// « Breaking.Bad.S01E02.720p.mkv » (convention Kodi), « Film CD1.avi ».
/// </summary>
public static partial class FileNameParser
{
    public static readonly IReadOnlySet<string> VideoExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".m4v", ".avi", ".mov", ".wmv", ".mpg", ".mpeg", ".m2ts", ".ts", ".vob", ".webm", ".ogv", ".flv", ".3gp", ".iso", ".divx", ".xvid",
    };

    /// <summary>Mots techniques retirés du titre (insensibles à la casse), complétables par l'utilisateur.</summary>
    public static readonly IReadOnlyList<string> DefaultNoiseWords =
    [
        "1080p", "1080i", "720p", "2160p", "4k", "uhd", "hdr", "hdr10", "dv", "sdr", "bluray", "blu-ray", "bdrip", "brrip", "dvdrip", "webrip", "web-dl", "webdl", "web",
        "hdtv", "x264", "x265", "h264", "h265", "hevc", "avc", "xvid", "divx", "aac", "ac3", "dts", "truehd", "atmos", "remux", "proper", "repack",
        "extended", "unrated", "directors", "cut", "multi", "vff", "vfq", "vf", "vostfr", "french", "truefrench", "subfrench", "english", "10bit", "8bit",
    ];

    public static bool IsVideo(string path) => VideoExtensions.Contains(Path.GetExtension(path));

    /// <param name="path">Chemin ou nom du fichier.</param>
    /// <param name="extraNoiseWords">Mots à retirer en plus des mots techniques usuels.</param>
    /// <param name="ignoreBracketed">
    /// Ignorer tout ce qui est entre ( ), [ ] ou { } pour le titre (« Heat (Director's Cut) [1080p] »).
    /// L'année, l'identifiant IMDb, la résolution et la partie y sont tout de même lus avant d'être retirés.
    /// </param>
    public static ParsedName Parse(string path, IEnumerable<string>? extraNoiseWords = null, bool ignoreBracketed = true)
    {
        ArgumentNullException.ThrowIfNull(path);
        var name = Path.GetFileNameWithoutExtension(path);
        var imdb = ImdbPattern().Match(name) is { Success: true } m ? m.Value : null;

        int? season = null, episode = null;
        var episodeMatch = EpisodePattern().Match(name);
        if (episodeMatch.Success)
        {
            season = int.Parse(episodeMatch.Groups["s"].Value, CultureInfo.InvariantCulture);
            episode = int.Parse(episodeMatch.Groups["e"].Value, CultureInfo.InvariantCulture);
            name = name[..episodeMatch.Index];
        }

        int? part = PartPattern().Match(name) is { Success: true } pm ? int.Parse(pm.Groups["n"].Value, CultureInfo.InvariantCulture) : null;
        var resolution = ResolutionPattern().Match(name) is { Success: true } rm ? rm.Value.ToLowerInvariant() : null;

        // Année : la dernière année plausible, pour garder « 2001 : l'odyssée... (1968) » ou « Blade Runner 2049 (2017) ».
        int? year = null;
        var cut = name.Length;
        foreach (Match y in YearPattern().Matches(name))
        {
            var value = int.Parse(y.Groups["y"].Value, CultureInfo.InvariantCulture);
            if (value >= 1888 && value <= DateTime.UtcNow.Year + 2 && y.Index > 0)
            {
                year = value;
                cut = y.Index;
            }
        }

        string title;
        if (ignoreBracketed)
        {
            // L'année a déjà été lue sur le nom complet ; ce qui la suit est technique. Sur ce qui la précède,
            // on retire les groupes entre délimiteurs, imbrications comprises.
            var stripped = name[..cut];
            string previous;
            do
            {
                previous = stripped;
                stripped = AnyBracketPattern().Replace(stripped, " ");
            }
            while (stripped != previous);

            title = UnclosedBracketPattern().Replace(stripped, " ");
        }
        else
        {
            title = name[..cut];
        }

        var noise = new HashSet<string>(DefaultNoiseWords.Concat(extraNoiseWords ?? []), StringComparer.OrdinalIgnoreCase);
        title = SeparatorPattern().Replace(title, " ");
        var words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

        // Sans année, on coupe au premier mot technique ; on retire aussi les marqueurs de partie.
        var firstNoise = words.FindIndex(w => noise.Contains(w) || ResolutionPattern().IsMatch(w));
        if (firstNoise > 0)
        {
            words = words[..firstNoise];
        }

        words.RemoveAll(w => noise.Contains(w) || PartPattern().IsMatch(w) || ImdbPattern().IsMatch(w));
        title = string.Join(' ', words).Trim(' ', '-', '_', '.');
        return new ParsedName(title.Length == 0 ? name : title, year, season, episode, part, resolution, imdb);
    }

    [GeneratedRegex(@"tt\d{7,10}")]
    private static partial Regex ImdbPattern();

    [GeneratedRegex(@"[Ss](?<s>\d{1,2})[ ._-]?[Ee](?<e>\d{1,3})|(?<s>\d{1,2})x(?<e>\d{2,3})(?!\d)")]
    private static partial Regex EpisodePattern();

    [GeneratedRegex(@"(?<![0-9])[\(\[ ._-]?(?<y>(18|19|20)\d{2})[\)\] ._-]?(?![0-9p])")]
    private static partial Regex YearPattern();

    [GeneratedRegex(@"(?i)\b(?:cd|disc|disk|part|pt)[ ._-]?(?<n>\d{1,2})\b")]
    private static partial Regex PartPattern();

    // Pas de \b : « _ » compte comme lettre pour \b, or « The_Thing_720p » doit être reconnu.
    [GeneratedRegex(@"(?i)(?<![a-z0-9])(2160|1080|720|576|480)[pi](?![a-z0-9])")]
    private static partial Regex ResolutionPattern();

    // Groupe le plus intérieur, répété jusqu'à disparition pour traiter les imbrications.
    [GeneratedRegex(@"\([^()\[\]{}]*\)|\[[^()\[\]{}]*\]|\{[^()\[\]{}]*\}")]
    private static partial Regex AnyBracketPattern();

    // Délimiteur ouvert jamais fermé (« Film (version longue.mkv ») : on retire jusqu'à la fin.
    [GeneratedRegex(@"[(\[{].*$")]
    private static partial Regex UnclosedBracketPattern();

    [GeneratedRegex(@"[._()\[\]{}\s]+")]
    private static partial Regex SeparatorPattern();
}
