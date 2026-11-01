using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Cinedex.Media;

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

    /// <summary>
    /// Termes propres aux fichiers vidéo diffusés (résolution, source, codecs, audio, langues, marqueurs de release),
    /// retirés du titre en mots entiers, sans tenir compte de la casse. Liste par défaut, modifiable dans les réglages.
    /// Un séparateur dans un terme (« WEB-DL », « DD5.1 », « H.264 ») accepte aussi espace, point, tiret ou soulignement.
    /// Les sigles courts ambigus (« HD », « TS », « MA », « EN », « It »...) en sont exclus : ce sont aussi des mots de titres.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultNoiseWords =
    [
        // Résolutions et formats d'image
        "2160p", "1440p", "1080p", "1080i", "720p", "576p", "576i", "480p", "480i", "4K", "UHD", "FHD", "HSBS", "HOU",
        // Sources
        "BluRay", "Blu-Ray", "BDRip", "BRRip", "BDMV", "BDRemux", "Remux", "UHDRip", "WEB", "WEB-DL", "WEBDL", "WEBRip", "WEB-Rip", "HDTV", "HDTVRip", "PDTV",
        "DVDRip", "DVD", "DVD5", "DVD9", "DVDScr", "DVDScreener", "Screener", "SCR", "HDRip", "HDCAM", "CAMRip", "TeleSync", "HDTS", "TeleCine",
        "VHSRip", "AMZN", "DSNP", "HMAX", "ATVP",
        // Vidéo
        "x264", "x265", "H264", "H265", "H.264", "H.265", "HEVC", "AVC", "AV1", "VP9", "XviD", "DivX", "VC-1", "MPEG2", "10bit", "10-bit", "8bit", "12bit",
        "HDR", "HDR10", "HDR10+", "HDR10Plus", "DoVi", "Dolby Vision", "HLG", "SDR",
        // Audio
        "AAC", "AAC2.0", "AAC5.1", "AC3", "EAC3", "E-AC3", "DD2.0", "DD5.1", "DD+", "DDP", "DDP5.1", "DDP7.1", "DTS", "DTS-HD", "DTS-HDMA", "DTS-MA", "DTS-X", "DTSX",
        "TrueHD", "Atmos", "FLAC", "LPCM", "PCM", "MP3", "Opus", "5.1", "7.1", "2.0",
        // Langues et sous-titres
        "MULTI", "VF", "VFF", "VFQ", "VFI", "VF2", "VOST", "VOSTFR", "SUBFRENCH", "FRENCH", "TRUEFRENCH", "VOF",
        // Marqueurs de release
        "PROPER", "REPACK", "RERIP", "iNTERNAL", "READNFO", "DUBBED", "SUBBED",
    ];

    private static readonly ConcurrentDictionary<string, Regex> NoisePatterns = new(StringComparer.Ordinal);

    public static bool IsVideo(string path, IReadOnlySet<string>? extensions = null) => (extensions ?? VideoExtensions).Contains(Path.GetExtension(path));

    /// <param name="path">Chemin ou nom du fichier.</param>
    /// <param name="noiseWords">Termes à retirer du titre ; null = <see cref="DefaultNoiseWords"/>.</param>
    /// <param name="ignoreBracketed">
    /// Ignorer tout ce qui est entre ( ), [ ] ou { } pour le titre (« Heat (Director's Cut) [1080p] »).
    /// L'année, l'identifiant IMDb, la résolution et la partie y sont tout de même lus avant d'être retirés.
    /// </param>
    public static ParsedName Parse(string path, IEnumerable<string>? noiseWords = null, bool ignoreBracketed = true)
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

        // Les termes techniques sont repérés sur le texte brut (ils peuvent contenir « . » ou « - »),
        // remplacés par une marque, puis le titre est coupé à la première marque : ce qui suit est technique.
        const string Mark = "\u0001";
        title = NoisePattern(noiseWords ?? DefaultNoiseWords).Replace(title, " " + Mark + " ");
        title = ResolutionPattern().Replace(title, " " + Mark + " ");
        title = SeparatorPattern().Replace(title, " ");
        var words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        var firstNoise = words.IndexOf(Mark);
        if (firstNoise > 0)
        {
            words = words[..firstNoise];
        }

        words.RemoveAll(w => w == Mark || PartPattern().IsMatch(w) || ImdbPattern().IsMatch(w));
        title = string.Join(' ', words).Trim(' ', '-', '_', '.');
        return new ParsedName(title.Length == 0 ? name : title, year, season, episode, part, resolution, imdb);
    }

    /// <summary>
    /// Expression « mots entiers » construite une fois par liste : chaque terme est échappé, et ses séparateurs
    /// internes (espace, point, tiret, soulignement) acceptent n'importe lequel de ces séparateurs, ou aucun.
    /// </summary>
    internal static Regex NoisePattern(IEnumerable<string> words)
    {
        var terms = words.Select(w => w.Trim()).Where(w => w.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(w => w.Length)
            .ToList();
        var key = string.Join('\n', terms);
        return NoisePatterns.GetOrAdd(key, _ =>
        {
            if (terms.Count == 0)
            {
                return new Regex("(?!)", RegexOptions.CultureInvariant);
            }

            var alternatives = terms.Select(t => string.Join("[ ._-]?", SeparatorPattern().Split(t).Where(p => p.Length > 0).Select(Regex.Escape)));
            return new Regex(
                $"(?<![\\p{{L}}\\p{{N}}])(?:{string.Join('|', alternatives)})(?![\\p{{L}}\\p{{N}}])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(200));
        });
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
