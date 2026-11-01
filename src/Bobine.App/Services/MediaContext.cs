using Bobine.Data;

namespace Bobine.App.Services;

/// <summary>Dépendances des fonctions liées aux fichiers vidéo (scan, lecture, vérification).</summary>
public sealed record MediaContext(MediaFileRepository Files, LibraryImportService Import, SettingsStore Settings, NfoService? Nfo = null, RatingsService? Ratings = null)
{
    public const string FfprobeKey = "media.ffprobe";
    public const string MediaInfoKey = "media.mediainfo";
    public const string PlayerKey = "media.player";
    public const string PlayerArgsKey = "media.playerArgs";
    public const string MarkSeenOnPlayKey = "media.markSeenOnPlay";
    public const string IgnoreBracketedKey = "scan.ignoreBracketed";
    public const string NoiseWordsKey = "scan.noiseWords";

    /// <summary>Liste des termes retirés des titres : celle des réglages, sinon la liste par défaut.</summary>
    public static IReadOnlyList<string> NoiseWords(SettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var stored = settings.Get(NoiseWordsKey);
        return stored is null ? Bobine.Media.FileNameParser.DefaultNoiseWords : SplitLines(stored);
    }

    public static IReadOnlyList<string> SplitLines(string text) =>
        (text ?? string.Empty).Split(['\n', '\r', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
