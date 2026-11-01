using Bobine.Data;

namespace Bobine.App.Services;

/// <summary>Dépendances des fonctions liées aux fichiers vidéo (scan, lecture, vérification).</summary>
public sealed record MediaContext(MediaFileRepository Files, LibraryImportService Import, SettingsStore Settings, NfoService? Nfo = null)
{
    public const string FfprobeKey = "media.ffprobe";
    public const string MediaInfoKey = "media.mediainfo";
    public const string PlayerKey = "media.player";
    public const string PlayerArgsKey = "media.playerArgs";
    public const string MarkSeenOnPlayKey = "media.markSeenOnPlay";
    public const string IgnoreBracketedKey = "scan.ignoreBracketed";
}
