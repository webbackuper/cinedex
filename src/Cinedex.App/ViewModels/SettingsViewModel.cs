using System.Collections.ObjectModel;
using System.Globalization;
using Cinedex.App.Security;
using Cinedex.App.Services;
using Cinedex.Core.Titles;
using Cinedex.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using Cinedex.Core.Localization;

namespace Cinedex.App.ViewModels;

/// <summary>Un chiffre clé de l'onglet Statistiques : valeur mise en forme et libellé.</summary>
public sealed record StatCard(string Value, string Label);

/// <summary>Une vue du bandeau gauche, affichée ou masquée.</summary>
public sealed partial class FilterToggle(QuickFilter value, string label, bool isShown) : ObservableObject
{
    public QuickFilter Value { get; } = value;

    public string Label { get; } = label;

    [ObservableProperty]
    public partial bool IsShown { get; set; } = isShown;
}

/// <summary>
/// Réglages de l'application, par onglets : général, bandeau gauche, import et scan, métadonnées, .nfo, lecture. Le jeton TMDb va dans le coffre du système ; il n'est jamais réaffiché,
/// seule sa présence est indiquée.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    public const string LanguageKey = "tmdb.language";
    public const string CountryKey = "tmdb.country";

    private readonly ISecretStore _secrets;
    private readonly SettingsStore _settings;

    private readonly string? _dataRoot;

    public SettingsViewModel(ISecretStore secrets, SettingsStore settings, string? dataRoot = null, CatalogStatistics? statistics = null)
    {
        Statistics = statistics;
        _secrets = secrets;
        _settings = settings;
        _dataRoot = dataRoot;

        // Général : apparence commune à tous les catalogues (si l'emplacement des données est connu).
        ThemeChoices = [new(AppTheme.System, "Comme le système"), new(AppTheme.Light, "Clair"), new(AppTheme.Dark, "Sombre")];
        SelectedTheme = ThemeChoices.Single(c => c.Value == (dataRoot is null ? AppTheme.System : Appearance.Load(dataRoot)));
        var style = dataRoot is null ? new AppearanceStyle() : Appearance.LoadStyle(dataRoot);
        IconSetChoices =
        [
            new(IconSet.Fine, "Trait fin"),
            new(IconSet.Bold, "Trait marqué"),
            new(IconSet.Duotone, "Deux tons"),
            new(IconSet.Classic, "Classique (Buddha)"),
        ];
        SelectedIconSet = IconSetChoices.Single(c => c.Value == style.Icons);
        MenuChoices = [.. Enum.GetValues<MenuColor>().Select(m => new ColorChoice<MenuColor>(m, MenuLabel(m), AppearanceStyle.MenuColors(m)))];
        SelectedMenu = MenuChoices.Single(c => c.Value == style.Menu);
        AccentChoices = [.. Enum.GetValues<AccentColor>().Select(a => new ColorChoice<AccentColor>(a, AccentLabel(a), AppearanceStyle.AccentColors(a)))];
        SelectedAccent = AccentChoices.Single(c => c.Value == style.Accent);
        FontChoices = [.. AppearanceStyle.Fonts.Select(f => new Choice<string>(f, f == AppearanceStyle.DefaultFont ? Tr.T("Inter (fournie avec Cinedex)") : f == "Système" ? Tr.T("Police du système") : f))];
        SelectedFont = FontChoices.Single(c => c.Value == style.Font);
        StartupChoices =
        [
            new(StartupWindowMode.Maximized, "Agrandie à la taille de l'écran"),
            new(StartupWindowMode.FullScreen, "Plein écran (F11 pour en sortir)"),
            new(StartupWindowMode.LastSize, "Taille et position de la dernière fois"),
        ];
        SelectedLanguage = Cinedex.Core.Localization.Tr.Languages.First(l => l.Code == (dataRoot is null ? Cinedex.Core.Localization.Tr.Current.Code : LanguagePreference.Load(dataRoot)));
        SelectedStartup = StartupChoices.Single(c => c.Value == (dataRoot is null ? StartupWindowMode.Maximized : WindowPlacement.Load(dataRoot).Mode));

        // Bandeau gauche.
        var display = DisplaySettings.Load(settings);
        MainFilterToggles = [.. MainWindowViewModel.AllMainFilters.Where(f => f.Value != QuickFilter.All)
            .Select(f => new FilterToggle(f.Value, f.Label, !display.HiddenFilters.Contains(f.Value)))];
        DefinitionFilterToggles = [.. MainWindowViewModel.AllDefinitionFilters
            .Select(f => new FilterToggle(f.Value, f.Label, !display.HiddenFilters.Contains(f.Value)))];
        ShowRuleSearch = display.ShowRuleSearch;
        ShowPeople = display.ShowPeople;
        HideAdult = display.HideAdult;
        ShowTooltips = display.ShowTooltips;
        var columns = ListColumns.Load(settings);
        ColumnToggles = [.. Enum.GetValues<ListColumn>().Select(c => new ColumnToggle(c, ColumnLabel(c), columns.Shows(c)))];
        ManageCollections = display.ManageCollections;
        JournalEnabled = ActivityJournal.Current.Enabled;

        // Import et scan.
        var scan = ScanSettings.Load(settings);
        ExtensionsText = string.Join(' ', scan.Extensions);
        MinimumMegabytes = scan.MinimumMegabytes;
        IgnoreKeywordsText = string.Join('\n', scan.IgnoreKeywords);
        Recursive = scan.Recursive;
        DefaultUseTmdb = scan.UseTmdb;
        DefaultReadProperties = scan.ReadProperties;
        DefaultWriteNfo = scan.WriteNfo;

        Language = settings.Get(LanguageKey) ?? "fr-FR";
        Country = settings.Get(CountryKey) ?? "FR";
        FfprobePath = settings.Get(Services.MediaContext.FfprobeKey) ?? string.Empty;
        MediaInfoPath = settings.Get(Services.MediaContext.MediaInfoKey) ?? string.Empty;
        PlayerPath = settings.Get(Services.MediaContext.PlayerKey) ?? string.Empty;
        PlayerArgs = settings.Get(Services.MediaContext.PlayerArgsKey) ?? string.Empty;
        MarkSeenOnPlay = settings.Get(Services.MediaContext.MarkSeenOnPlayKey) != "false";
        IgnoreBracketed = settings.Get(Services.MediaContext.IgnoreBracketedKey) != "false";
        NoiseWordsText = string.Join('\n', Services.MediaContext.NoiseWords(settings));
        var nfo = Services.NfoSettings.Load(settings);
        NfoReadOnScan = nfo.ReadOnScan;
        NfoWriteAfterMetadata = nfo.WriteAfterMetadata;
        NfoOverwrite = nfo.OverwriteExisting;
        NfoBackup = nfo.Backup;
        HasToken = SafeRead() is not null;
        HasOmdbKey = SafeRead(SecretStore.OmdbKey) is not null;
        UseImdbDataset = settings.Get(Services.RatingsService.UseDatasetKey) != "false";
        MaxActorsNumber = Services.MetadataService.ReadMaxActors(settings);

        // Économiseur d'écran.
        var saver = ScreenSaverSettings.Load(settings);
        ScreenSaverEnabled = saver.Enabled;
        ScreenSaverIdleMinutes = saver.IdleMinutes;
        ScreenSaverIntervalSeconds = saver.IntervalSeconds;
        ScreenSaverShowTitles = saver.ShowTitles;
    }

    /// <summary>Onglet Maintenance (absent hors catalogue, par exemple en test).</summary>
    public MaintenanceViewModel? Maintenance { get; init; }

    public bool HasMaintenance => Maintenance is not null;

    /// <summary>Onglet Export de la liste des films (absent hors catalogue).</summary>
    public ExportViewModel? Export { get; init; }

    public bool HasExport => Export is not null;

    /// <summary>Onglet Sauvegardes (absent hors catalogue).</summary>
    public BackupsViewModel? Backups { get; init; }

    public bool HasBackups => Backups is not null;

    public IReadOnlyList<Choice<AppTheme>> ThemeChoices { get; }

    public IReadOnlyList<Choice<IconSet>> IconSetChoices { get; }

    [ObservableProperty]
    public partial Choice<IconSet> SelectedIconSet { get; set; }

    public IReadOnlyList<ColorChoice<MenuColor>> MenuChoices { get; }

    [ObservableProperty]
    public partial ColorChoice<MenuColor> SelectedMenu { get; set; }

    public IReadOnlyList<ColorChoice<AccentColor>> AccentChoices { get; }

    [ObservableProperty]
    public partial ColorChoice<AccentColor> SelectedAccent { get; set; }

    public IReadOnlyList<Choice<string>> FontChoices { get; }

    [ObservableProperty]
    public partial Choice<string> SelectedFont { get; set; }

    /// <summary>Aperçu de la police choisie dans l'onglet Apparence.</summary>
    public Avalonia.Media.FontFamily FontPreview => SelectedFont.Value switch
    {
        AppearanceStyle.DefaultFont => new Avalonia.Media.FontFamily("fonts:Inter#Inter"),
        "Système" => Avalonia.Media.FontFamily.Default,
        var name => new Avalonia.Media.FontFamily(name),
    };

    partial void OnSelectedFontChanged(Choice<string> value) => OnPropertyChanged(nameof(FontPreview));

    private static string MenuLabel(MenuColor menu) => menu switch
    {
        MenuColor.Slate => Tr.T("Ardoise"),
        MenuColor.Sage => Tr.T("Sauge"),
        MenuColor.Lavender => Tr.T("Lavande"),
        MenuColor.Rose => Tr.T("Rose poudré"),
        MenuColor.White => Tr.T("Blanc"),
        _ => Tr.T("Sable"),
    };

    private static string AccentLabel(AccentColor accent) => accent switch
    {
        AccentColor.Blue => Tr.T("Bleu"),
        AccentColor.Green => Tr.T("Vert"),
        AccentColor.Violet => Tr.T("Violet"),
        AccentColor.Red => Tr.T("Rouge"),
        AccentColor.Graphite => Tr.T("Graphite"),
        _ => Tr.T("Terre cuite"),
    };

    [ObservableProperty]
    public partial Choice<AppTheme> SelectedTheme { get; set; }

    public bool CanChooseTheme => _dataRoot is not null;

    public IReadOnlyList<Choice<StartupWindowMode>> StartupChoices { get; }

    [ObservableProperty]
    public partial Choice<StartupWindowMode> SelectedStartup { get; set; }

    /// <summary>Les langues proposées, chacune écrite dans sa propre langue.</summary>
    public static IReadOnlyList<Cinedex.Core.Localization.UiLanguage> LanguageChoices => Cinedex.Core.Localization.Tr.Languages;

    [ObservableProperty]
    public partial Cinedex.Core.Localization.UiLanguage SelectedLanguage { get; set; }

    /// <summary>Vrai après un enregistrement qui change la langue : elle s'applique au redémarrage.</summary>
    public bool LanguageChanged { get; private set; }

    public string DataLocation => _dataRoot ?? "(inconnu)";

    public ObservableCollection<FilterToggle> MainFilterToggles { get; }

    public ObservableCollection<FilterToggle> DefinitionFilterToggles { get; }

    [ObservableProperty]
    public partial bool ShowRuleSearch { get; set; }

    [ObservableProperty]
    public partial bool ShowPeople { get; set; }

    [ObservableProperty]
    public partial bool HideAdult { get; set; }

    [ObservableProperty]
    public partial bool ShowTooltips { get; set; }

    /// <summary>Colonnes de la vue liste, cochées si affichées.</summary>
    public IReadOnlyList<ColumnToggle> ColumnToggles { get; }

    internal static string ColumnLabel(ListColumn column) => column switch
    {
        ListColumn.Poster => Tr.T("Affiche"),
        ListColumn.Year => Tr.T("Année"),
        ListColumn.Kind => Tr.T("Mention « Série »"),
        ListColumn.Medium => Tr.T("Support"),
        ListColumn.Runtime => Tr.T("Durée"),
        ListColumn.Genres => Tr.T("Genres"),
        ListColumn.Imdb => Tr.T("Note IMDb"),
        ListColumn.UserRating => Tr.T("Ma note"),
        ListColumn.Loan => Tr.T("Prêt (emprunteur)"),
        ListColumn.Favorite => Tr.T("Favori"),
        ListColumn.Seen => Tr.T("Vu"),
        _ => Tr.T("Numéro"),
    };

    /// <summary>Collections (sagas TMDb) : lien du bandeau gauche, fiche et décors téléchargés après chaque import.</summary>
    [ObservableProperty]
    public partial bool ManageCollections { get; set; }

    /// <summary>Journal des actions (fichier journal-AAAA-MM-JJ.log du dossier des journaux).</summary>
    [ObservableProperty]
    public partial bool JournalEnabled { get; set; }

    [ObservableProperty]
    public partial string? JournalMessage { get; set; }

    public static string JournalFolder => ActivityJournal.Current.LogsDirectory;

    /// <summary>Statistiques du catalogue ouvert (onglet Statistiques) ; absentes hors catalogue.</summary>
    public CatalogStatistics? Statistics { get; }

    public bool HasStatistics => Statistics is not null;

    public IReadOnlyList<StatCard> StatCards => Statistics is not { } s ? [] :
    [
        new(Number(s.Films), Tr.T("Films")),
        new(Number(s.Series), Tr.T("Séries")),
        new(Number(s.Episodes), Tr.T("Épisodes")),
        new(Number(s.Actors), Tr.T("Acteurs")),
        new(Number(s.Directors), Tr.T("Réalisateurs")),
        new(Number(s.Seen), Tr.T("Vus")),
        new(Number(s.Favorites), Tr.T("Favoris")),
        new(Number(s.Loaned), Tr.T("Prêtés")),
        new(Number(s.Files), Tr.T("Fichiers vidéo")),
        new(Size(s.TotalBytes), Tr.T("Taille des fichiers")),
        new(Number(s.FilmMinutes / 60) + " h", Tr.T("Durée totale des films")),
        new(Number(s.LinkedToTmdb), Tr.T("Liés à TMDb")),
        new(Number(s.ToReview), Tr.T("À vérifier")),
    ];

    private static string Number(long value) => value.ToString("N0", CultureInfo.CurrentCulture);

    private static string Size(long bytes) => bytes >= 1L << 40
        ? Tr.F("{0:0.00} To", bytes / (double)(1L << 40))
        : Tr.F("{0:0.0} Go", bytes / (double)(1L << 30));

    /// <summary>Extensions des fichiers vidéo retenus, séparées par des espaces (« .mkv .mp4 »).</summary>
    [ObservableProperty]
    public partial string ExtensionsText { get; set; }

    public void ResetExtensions() => ExtensionsText = string.Join(' ', new ScanSettings().Extensions);

    [ObservableProperty]
    public partial decimal? MinimumMegabytes { get; set; }

    /// <summary>Fichiers ignorés au scan s'ils contiennent l'un de ces mots, un par ligne.</summary>
    [ObservableProperty]
    public partial string IgnoreKeywordsText { get; set; }

    [ObservableProperty]
    public partial bool Recursive { get; set; }

    [ObservableProperty]
    public partial bool DefaultUseTmdb { get; set; }

    [ObservableProperty]
    public partial bool DefaultReadProperties { get; set; }

    [ObservableProperty]
    public partial bool DefaultWriteNfo { get; set; }

    [ObservableProperty]
    public partial string FfprobePath { get; set; }

    [ObservableProperty]
    public partial string MediaInfoPath { get; set; }

    [ObservableProperty]
    public partial string PlayerPath { get; set; }

    [ObservableProperty]
    public partial string PlayerArgs { get; set; }

    [ObservableProperty]
    public partial bool MarkSeenOnPlay { get; set; }

    [ObservableProperty]
    public partial bool IgnoreBracketed { get; set; }

    /// <summary>Termes retirés des titres à l'import, un par ligne.</summary>
    [ObservableProperty]
    public partial string NoiseWordsText { get; set; }

    public void ResetNoiseWords() => NoiseWordsText = string.Join('\n', Cinedex.Media.FileNameParser.DefaultNoiseWords);

    [ObservableProperty]
    public partial bool NfoReadOnScan { get; set; }

    [ObservableProperty]
    public partial bool NfoWriteAfterMetadata { get; set; }

    [ObservableProperty]
    public partial bool NfoOverwrite { get; set; }

    [ObservableProperty]
    public partial bool NfoBackup { get; set; }

    [ObservableProperty]
    public partial string NewToken { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Language { get; set; }

    [ObservableProperty]
    public partial string Country { get; set; }

    [ObservableProperty]
    public partial bool HasToken { get; set; }

    /// <summary>Nouvelle clé OMDb (jamais réaffichée une fois enregistrée).</summary>
    [ObservableProperty]
    public partial string NewOmdbKey { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OmdbState))]
    public partial bool HasOmdbKey { get; set; }

    [ObservableProperty]
    public partial bool UseImdbDataset { get; set; }

    /// <summary>Diaporama des affiches après une période d'inactivité, quand la fenêtre de Cinedex est active.</summary>
    [ObservableProperty]
    public partial bool ScreenSaverEnabled { get; set; }

    [ObservableProperty]
    public partial decimal? ScreenSaverIdleMinutes { get; set; }

    [ObservableProperty]
    public partial decimal? ScreenSaverIntervalSeconds { get; set; }

    [ObservableProperty]
    public partial bool ScreenSaverShowTitles { get; set; }

    /// <summary>Nombre d'acteurs gardés dans la distribution à chaque import ou mise à jour TMDb.</summary>
    [ObservableProperty]
    public partial decimal? MaxActorsNumber { get; set; }

    public string OmdbState => HasOmdbKey
        ? "Une clé OMDb est enregistrée dans le coffre du système (notes IMDb, Rotten Tomatoes, Metacritic)."
        : "Aucune clé OMDb : créez-en une gratuite sur omdbapi.com pour les notes Rotten Tomatoes et Metacritic.";

    [ObservableProperty]
    public partial string? Error { get; set; }

    public string TokenState => HasToken
        ? "Un jeton est enregistré dans le coffre du système. Saisissez-en un nouveau pour le remplacer."
        : "Aucun jeton enregistré. Créez-en un sur themoviedb.org, Paramètres, API, « Jeton d'accès en lecture ».";

    partial void OnHasTokenChanged(bool value) => OnPropertyChanged(nameof(TokenState));

    /// <summary>Valide et enregistre ; renvoie false avec <see cref="Error"/> renseigné si la saisie est invalide.</summary>
    public bool Save()
    {
        var token = NewToken.Trim();
        if (token.Length > 0 && (token.Length < 32 || token.Length > 2048 || token.Any(char.IsWhiteSpace)))
        {
            Error = "Le jeton ne semble pas valide : collez le « jeton d'accès en lecture » (long texte commençant par eyJ).";
            return false;
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(Language, "^[a-z]{2}(-[A-Z]{2})?$") ||
            !System.Text.RegularExpressions.Regex.IsMatch(Country, "^[A-Z]{2}$"))
        {
            Error = "Langue attendue au format fr-FR, pays au format FR.";
            return false;
        }

        foreach (var (path, label) in new[] { (MediaInfoPath, "MediaInfo"), (FfprobePath, "ffprobe"), (PlayerPath, "lecteur externe") })
        {
            if (path.Trim().Length > 0 && !File.Exists(path.Trim()))
            {
                Error = Tr.F("Chemin du {0} introuvable : {1}", label, path);
                return false;
            }
        }

        var maxActors = (int)Math.Round(MaxActorsNumber ?? 0);
        if (maxActors is < 1 or > 200)
        {
            Error = "Nombre d'acteurs : un nombre entre 1 et 200 est attendu.";
            return false;
        }

        var idleMinutes = (int)Math.Round(ScreenSaverIdleMinutes ?? 0);
        if (idleMinutes is < ScreenSaverSettings.MinIdleMinutes or > ScreenSaverSettings.MaxIdleMinutes)
        {
            Error = Tr.F("Économiseur d'écran : une inactivité entre {0} et {1} minutes est attendue.", ScreenSaverSettings.MinIdleMinutes, ScreenSaverSettings.MaxIdleMinutes);
            return false;
        }

        var intervalSeconds = (int)Math.Round(ScreenSaverIntervalSeconds ?? 0);
        if (intervalSeconds is < ScreenSaverSettings.MinIntervalSeconds or > ScreenSaverSettings.MaxIntervalSeconds)
        {
            Error = Tr.F("Économiseur d'écran : un intervalle entre {0} et {1} secondes est attendu.", ScreenSaverSettings.MinIntervalSeconds, ScreenSaverSettings.MaxIntervalSeconds);
            return false;
        }

        var extensions = ScanSettings.NormalizeExtensions(ExtensionsText);
        if (extensions.Count == 0)
        {
            Error = "Indiquez au moins une extension de fichier vidéo (ex. .mkv .mp4 .avi).";
            return false;
        }

        var minimum = (int)Math.Round(MinimumMegabytes ?? 0);
        if (minimum is < 0 or > 100_000)
        {
            Error = "Taille minimale : un nombre de Mo entre 0 et 100 000 est attendu.";
            return false;
        }

        var omdb = NewOmdbKey.Trim();
        if (omdb.Length > 0 && (omdb.Length is < 6 or > 64 || !omdb.All(char.IsAsciiLetterOrDigit)))
        {
            Error = "La clé OMDb ne semble pas valide (lettres et chiffres, ex. 8 caractères).";
            return false;
        }

        if (omdb.Length > 0)
        {
            _secrets.Write(SecretStore.OmdbKey, omdb);
            NewOmdbKey = string.Empty;
            HasOmdbKey = true;
        }

        _settings.Set(Services.RatingsService.UseDatasetKey, UseImdbDataset ? "true" : "false");
        _settings.Set(Services.MetadataService.MaxActorsKey, maxActors.ToString(CultureInfo.InvariantCulture));
        new ScreenSaverSettings
        {
            Enabled = ScreenSaverEnabled,
            IdleMinutes = idleMinutes,
            IntervalSeconds = intervalSeconds,
            ShowTitles = ScreenSaverShowTitles,
        }.Save(_settings);

        if (token.Length > 0)
        {
            _secrets.Write(SecretStore.TmdbToken, token);
            NewToken = string.Empty;
            HasToken = true;
        }

        _settings.Set(LanguageKey, Language);
        _settings.Set(CountryKey, Country);
        _settings.Set(Services.MediaContext.FfprobeKey, NullIfBlank(FfprobePath));
        _settings.Set(Services.MediaContext.MediaInfoKey, NullIfBlank(MediaInfoPath));
        _settings.Set(Services.MediaContext.PlayerKey, NullIfBlank(PlayerPath));
        _settings.Set(Services.MediaContext.PlayerArgsKey, NullIfBlank(PlayerArgs));
        _settings.Set(Services.MediaContext.MarkSeenOnPlayKey, MarkSeenOnPlay ? "true" : "false");
        new ScanSettings
        {
            Extensions = extensions,
            MinimumMegabytes = minimum,
            IgnoreKeywords = MediaContext.SplitLines(IgnoreKeywordsText),
            Recursive = Recursive,
            IgnoreBracketed = IgnoreBracketed,
            NoiseWords = MediaContext.SplitLines(NoiseWordsText),
            UseTmdb = DefaultUseTmdb,
            ReadProperties = DefaultReadProperties,
            WriteNfo = DefaultWriteNfo,
        }.Save(_settings);
        new DisplaySettings
        {
            HiddenFilters = MainFilterToggles.Concat(DefinitionFilterToggles).Where(t => !t.IsShown).Select(t => t.Value).ToHashSet(),
            ShowRuleSearch = ShowRuleSearch,
            ShowPeople = ShowPeople,
            HideAdult = HideAdult,
            ShowTooltips = ShowTooltips,
            ManageCollections = ManageCollections,
        }.Save(_settings);
        new ListColumns { Hidden = ColumnToggles.Where(t => !t.IsShown).Select(t => t.Value).ToHashSet() }.Save(_settings);
        Backups?.Save();
        ActivityJournal.Current.SetEnabled(JournalEnabled);
        if (_dataRoot is not null)
        {
            Appearance.Save(_dataRoot, SelectedTheme.Value);
            Appearance.Apply(SelectedTheme.Value);
            var style = new AppearanceStyle { Icons = SelectedIconSet.Value, Menu = SelectedMenu.Value, Accent = SelectedAccent.Value, Font = SelectedFont.Value };
            Appearance.SaveStyle(_dataRoot, style);
            Appearance.ApplyStyle(style);
            (WindowPlacement.Load(_dataRoot) with { Mode = SelectedStartup.Value }).Save(_dataRoot);
            LanguageChanged = SelectedLanguage.Code != Cinedex.Core.Localization.Tr.Current.Code;
            LanguagePreference.Save(_dataRoot, SelectedLanguage.Code);
        }

        _settings.Set(Services.NfoSettings.ReadKey, NfoReadOnScan ? "true" : "false");
        _settings.Set(Services.NfoSettings.WriteKey, NfoWriteAfterMetadata ? "true" : "false");
        _settings.Set(Services.NfoSettings.OverwriteKey, NfoOverwrite ? "true" : "false");
        _settings.Set(Services.NfoSettings.BackupKey, NfoBackup ? "true" : "false");
        Error = null;
        return true;
    }

    private static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public void ForgetToken()
    {
        _secrets.Delete(SecretStore.TmdbToken);
        HasToken = false;
    }

    private string? SafeRead(string name = SecretStore.TmdbToken)
    {
        try
        {
            return _secrets.Read(name);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}

/// <summary>Choix de couleur de l'onglet Apparence : libellé et pastilles en thème clair et sombre.</summary>
public sealed record ColorChoice<T>(T Value, string Label, (Avalonia.Media.Color Light, Avalonia.Media.Color Dark) Colors)
{
    public Avalonia.Media.IBrush LightBrush { get; } = new Avalonia.Media.SolidColorBrush(Colors.Light);

    public Avalonia.Media.IBrush DarkBrush { get; } = new Avalonia.Media.SolidColorBrush(Colors.Dark);
}

/// <summary>Une colonne de la vue liste et son état (affichée ou non).</summary>
public sealed partial class ColumnToggle(ListColumn value, string label, bool isShown) : ObservableObject
{
    public ListColumn Value { get; } = value;

    public string Label { get; } = label;

    [ObservableProperty]
    public partial bool IsShown { get; set; } = isShown;
}
