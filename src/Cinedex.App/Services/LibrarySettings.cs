using System.Globalization;
using Cinedex.Core.Titles;
using Cinedex.Data;
using Cinedex.Media;

namespace Cinedex.App.Services;

/// <summary>
/// Réglages d'import et de scan du catalogue : quels fichiers sont retenus, comment leur nom est lu, et les options
/// cochées par défaut dans la fenêtre d'import. Une valeur absente de la base garde sa valeur par défaut.
/// </summary>
public sealed record ScanSettings
{
    public const string ExtensionsKey = "scan.extensions";
    public const string MinimumMegabytesKey = "scan.minimumMb";
    public const string IgnoreKeywordsKey = "scan.ignoreKeywords";
    public const string RecursiveKey = "scan.recursive";
    public const string UseTmdbKey = "import.useTmdb";
    public const string ReadPropertiesKey = "import.readProperties";
    public const string WriteNfoKey = "import.writeNfo";

    public static IReadOnlyList<string> DefaultIgnoreKeywords { get; } = ["sample", "trailer", "-ts.", "featurette"];

    public IReadOnlyList<string> Extensions { get; init; } = [.. FileNameParser.VideoExtensions.Order(StringComparer.OrdinalIgnoreCase)];

    /// <summary>Taille minimale d'un fichier trouvé en parcourant un dossier (les petits fichiers sont souvent des extraits).</summary>
    public int MinimumMegabytes { get; init; } = 20;

    public IReadOnlyList<string> IgnoreKeywords { get; init; } = DefaultIgnoreKeywords;

    public bool Recursive { get; init; } = true;

    public bool IgnoreBracketed { get; init; } = true;

    public IReadOnlyList<string> NoiseWords { get; init; } = FileNameParser.DefaultNoiseWords;

    public bool UseTmdb { get; init; }

    public bool ReadProperties { get; init; } = true;

    public bool WriteNfo { get; init; }

    public IReadOnlySet<string> ExtensionSet => new HashSet<string>(Extensions, StringComparer.OrdinalIgnoreCase);

    public ScanOptions ToScanOptions() => new()
    {
        Recursive = Recursive,
        IgnoreBracketed = IgnoreBracketed,
        NoiseWords = NoiseWords,
        IgnoreKeywords = IgnoreKeywords,
        MinimumBytes = MinimumMegabytes * 1024L * 1024,
        VideoExtensions = ExtensionSet,
    };

    public static ScanSettings Load(SettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var defaults = new ScanSettings();
        return new ScanSettings
        {
            Extensions = settings.Get(ExtensionsKey) is { } ext ? NormalizeExtensions(ext) : defaults.Extensions,
            MinimumMegabytes = int.TryParse(settings.Get(MinimumMegabytesKey), NumberStyles.Integer, CultureInfo.InvariantCulture, out var mb) && mb >= 0
                ? mb
                : defaults.MinimumMegabytes,
            IgnoreKeywords = settings.Get(IgnoreKeywordsKey) is { } kw ? MediaContext.SplitLines(kw) : defaults.IgnoreKeywords,
            Recursive = settings.Get(RecursiveKey) != "false",
            IgnoreBracketed = settings.Get(MediaContext.IgnoreBracketedKey) != "false",
            NoiseWords = MediaContext.NoiseWords(settings),
            UseTmdb = settings.Get(UseTmdbKey) == "true",
            ReadProperties = settings.Get(ReadPropertiesKey) != "false",
            WriteNfo = settings.Get(WriteNfoKey) == "true",
        };
    }

    public void Save(SettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Set(ExtensionsKey, string.Join(' ', Extensions));
        settings.Set(MinimumMegabytesKey, MinimumMegabytes.ToString(CultureInfo.InvariantCulture));
        settings.Set(IgnoreKeywordsKey, string.Join('\n', IgnoreKeywords));
        settings.Set(RecursiveKey, Flag(Recursive));
        settings.Set(MediaContext.IgnoreBracketedKey, Flag(IgnoreBracketed));
        settings.Set(MediaContext.NoiseWordsKey, string.Join('\n', NoiseWords));
        settings.Set(UseTmdbKey, Flag(UseTmdb));
        settings.Set(ReadPropertiesKey, Flag(ReadProperties));
        settings.Set(WriteNfoKey, Flag(WriteNfo));
    }

    /// <summary>« mkv, .MP4 avi » donne « .avi .mkv .mp4 » : point ajouté, minuscules, sans doublon, triées.</summary>
    public static IReadOnlyList<string> NormalizeExtensions(string text) =>
        (text ?? string.Empty).Split([' ', ',', ';', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => "." + e.TrimStart('*', '.').ToLowerInvariant())
            .Where(e => e.Length > 1 && e.Length <= 10 && e.Skip(1).All(char.IsAsciiLetterOrDigit))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

    internal static string Flag(bool value) => value ? "true" : "false";
}

/// <summary>Affichage : vues du bandeau gauche, liens, contenu pour adultes. Propre à chaque catalogue.</summary>
public sealed record DisplaySettings
{
    public const string HiddenFiltersKey = "ui.hiddenFilters";
    public const string ShowRuleSearchKey = "ui.showRuleSearch";
    public const string ShowPeopleKey = "ui.showPeople";
    public const string HideAdultKey = "ui.hideAdult";

    /// <summary>Vues masquées dans le bandeau gauche (« Tous les titres » reste toujours visible).</summary>
    public IReadOnlySet<QuickFilter> HiddenFilters { get; init; } = new HashSet<QuickFilter>();

    public bool ShowRuleSearch { get; init; } = true;

    public bool ShowPeople { get; init; } = true;

    public bool HideAdult { get; init; }

    public static DisplaySettings Load(SettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var hidden = (settings.Get(HiddenFiltersKey) ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(v => Enum.TryParse<QuickFilter>(v, out var f) ? f : (QuickFilter?)null)
            .OfType<QuickFilter>()
            .Where(f => f != QuickFilter.All)
            .ToHashSet();
        return new DisplaySettings
        {
            HiddenFilters = hidden,
            ShowRuleSearch = settings.Get(ShowRuleSearchKey) != "false",
            ShowPeople = settings.Get(ShowPeopleKey) != "false",
            HideAdult = settings.Get(HideAdultKey) == "true",
        };
    }

    public void Save(SettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Set(HiddenFiltersKey, string.Join(',', HiddenFilters.Where(f => f != QuickFilter.All).Order()));
        settings.Set(ShowRuleSearchKey, ScanSettings.Flag(ShowRuleSearch));
        settings.Set(ShowPeopleKey, ScanSettings.Flag(ShowPeople));
        settings.Set(HideAdultKey, ScanSettings.Flag(HideAdult));
    }
}
