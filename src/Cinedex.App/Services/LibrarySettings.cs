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
    public const string ShowDirectorsKey = "ui.showDirectors";
    public const string HideAdultKey = "ui.hideAdult";
    public const string ShowTooltipsKey = "ui.showTooltips";
    public const string CollectionsKey = "collections.enabled";

    /// <summary>Vues masquées dans le bandeau gauche (« Tous les titres » reste toujours visible).</summary>
    public IReadOnlySet<QuickFilter> HiddenFilters { get; init; } = new HashSet<QuickFilter>();

    public bool ShowRuleSearch { get; init; } = true;

    public bool ShowPeople { get; init; } = true;

    /// <summary>Lien « Réalisateurs » du bandeau gauche (activé par défaut).</summary>
    public bool ShowDirectors { get; init; } = true;

    public bool HideAdult { get; init; }

    /// <summary>Infobulle (titre, genres, note IMDb, synopsis) au survol d'un film de la liste ; désactivée par défaut.</summary>
    public bool ShowTooltips { get; init; }

    /// <summary>
    /// Gestion des collections (sagas TMDb) : lien « Collections » du bandeau gauche, fiche et décors des sagas
    /// téléchargés depuis TMDb après chaque import. Activée par défaut.
    /// </summary>
    public bool ManageCollections { get; init; } = true;

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
            ShowDirectors = settings.Get(ShowDirectorsKey) != "false",
            HideAdult = settings.Get(HideAdultKey) == "true",
            ShowTooltips = settings.Get(ShowTooltipsKey) == "true",
            ManageCollections = settings.Get(CollectionsKey) != "false",
        };
    }

    public void Save(SettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Set(HiddenFiltersKey, string.Join(',', HiddenFilters.Where(f => f != QuickFilter.All).Order()));
        settings.Set(ShowRuleSearchKey, ScanSettings.Flag(ShowRuleSearch));
        settings.Set(ShowPeopleKey, ScanSettings.Flag(ShowPeople));
        settings.Set(ShowDirectorsKey, ScanSettings.Flag(ShowDirectors));
        settings.Set(HideAdultKey, ScanSettings.Flag(HideAdult));
        settings.Set(ShowTooltipsKey, ScanSettings.Flag(ShowTooltips));
        settings.Set(CollectionsKey, ScanSettings.Flag(ManageCollections));
    }
}

/// <summary>
/// Économiseur d'écran : diaporama des affiches lancé après une période d'inactivité, quand la fenêtre de Cinedex
/// est active. Propre à chaque catalogue ; activé par défaut.
/// </summary>
public sealed record ScreenSaverSettings
{
    public const string EnabledKey = "screensaver.enabled";
    public const string IdleMinutesKey = "screensaver.idleMinutes";
    public const string IntervalSecondsKey = "screensaver.intervalSeconds";
    public const string ShowTitlesKey = "screensaver.showTitles";

    public const int MinIdleMinutes = 1;
    public const int MaxIdleMinutes = 240;
    public const int MinIntervalSeconds = 2;
    public const int MaxIntervalSeconds = 300;

    public bool Enabled { get; init; } = true;

    /// <summary>Minutes sans clavier ni souris avant le diaporama (1 à 240).</summary>
    public int IdleMinutes { get; init; } = 5;

    /// <summary>Secondes d'affichage de chaque affiche (2 à 300).</summary>
    public int IntervalSeconds { get; init; } = 8;

    /// <summary>Titre et année sous chaque affiche.</summary>
    public bool ShowTitles { get; init; } = true;

    public static ScreenSaverSettings Load(SettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new ScreenSaverSettings
        {
            Enabled = settings.Get(EnabledKey) != "false",
            IdleMinutes = Read(settings, IdleMinutesKey, 5, MinIdleMinutes, MaxIdleMinutes),
            IntervalSeconds = Read(settings, IntervalSecondsKey, 8, MinIntervalSeconds, MaxIntervalSeconds),
            ShowTitles = settings.Get(ShowTitlesKey) != "false",
        };
    }

    public void Save(SettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Set(EnabledKey, ScanSettings.Flag(Enabled));
        settings.Set(IdleMinutesKey, Math.Clamp(IdleMinutes, MinIdleMinutes, MaxIdleMinutes).ToString(System.Globalization.CultureInfo.InvariantCulture));
        settings.Set(IntervalSecondsKey, Math.Clamp(IntervalSeconds, MinIntervalSeconds, MaxIntervalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture));
        settings.Set(ShowTitlesKey, ScanSettings.Flag(ShowTitles));
    }

    private static int Read(SettingsStore settings, string key, int fallback, int min, int max) =>
        int.TryParse(settings.Get(key), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, min, max)
            : fallback;
}

/// <summary>Colonnes (informations) affichables dans la vue liste de la fenêtre principale.</summary>
public enum ListColumn
{
    Poster,
    Year,
    Kind,
    Medium,
    Runtime,
    Genres,
    Imdb,
    UserRating,
    Loan,
    Favorite,
    Seen,
    Number,
    OriginalTitle,
    Studio,
    Certification,
    MediaId,
    Collection,
    AddedOn,
}

/// <summary>Colonnes affichées dans la vue liste ; propre à chaque catalogue. Genres et note IMDb sont masqués par défaut.</summary>
public sealed record ListColumns
{
    public const string HiddenKey = "ui.hiddenColumns";

    public static IReadOnlySet<ListColumn> DefaultHidden { get; } = new HashSet<ListColumn>
    {
        ListColumn.Genres, ListColumn.Imdb, ListColumn.OriginalTitle, ListColumn.Studio,
        ListColumn.Certification, ListColumn.MediaId, ListColumn.Collection, ListColumn.AddedOn,
    };

    public IReadOnlySet<ListColumn> Hidden { get; init; } = DefaultHidden;

    public bool Shows(ListColumn column) => !Hidden.Contains(column);

    public bool Poster => Shows(ListColumn.Poster);

    public bool Imdb => Shows(ListColumn.Imdb);

    public bool UserRating => Shows(ListColumn.UserRating);

    public bool Loan => Shows(ListColumn.Loan);

    public bool Favorite => Shows(ListColumn.Favorite);

    public bool Seen => Shows(ListColumn.Seen);

    public bool Number => Shows(ListColumn.Number);

    /// <summary>
    /// Ligne secondaire d'un titre : chaque colonne cochée dans les réglages y ajoute sa valeur, dans l'ordre
    /// de la liste des colonnes.
    /// </summary>
    public string Subtitle(Cinedex.Core.Titles.TitleSummary title)
    {
        ArgumentNullException.ThrowIfNull(title);
        return string.Join(" · ", new[]
        {
            Shows(ListColumn.Year) ? title.Year?.ToString(System.Globalization.CultureInfo.CurrentCulture) : null,
            Shows(ListColumn.Kind) && title.Kind == Cinedex.Core.Titles.TitleKind.Series ? Cinedex.Core.Localization.Tr.T("Série") : null,
            Shows(ListColumn.OriginalTitle) && !string.Equals(title.OriginalTitle, title.Title, StringComparison.Ordinal) ? title.OriginalTitle : null,
            Shows(ListColumn.Medium) ? title.Medium : null,
            Shows(ListColumn.Runtime) ? Cinedex.Core.Titles.TitleText.FormatRuntime(title.RuntimeMinutes) : null,
            Shows(ListColumn.Genres) && title.HasGenres ? title.GenresText : null,
            Shows(ListColumn.Studio) ? title.Studio : null,
            Shows(ListColumn.Certification) ? title.Certification : null,
            Shows(ListColumn.Collection) ? title.CollectionName : null,
            Shows(ListColumn.MediaId) ? title.MediaId : null,
            Shows(ListColumn.AddedOn) ? title.AddedOn : null,
        }.Where(p => !string.IsNullOrEmpty(p)));
    }

    public static ListColumns Load(SettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var stored = settings.Get(HiddenKey);
        if (stored is null)
        {
            return new ListColumns();
        }

        return new ListColumns
        {
            Hidden = stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(v => Enum.TryParse<ListColumn>(v, out var c) ? c : (ListColumn?)null)
                .OfType<ListColumn>()
                .ToHashSet(),
        };
    }

    public void Save(SettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Set(HiddenKey, string.Join(',', Hidden.Order()));
    }
}
