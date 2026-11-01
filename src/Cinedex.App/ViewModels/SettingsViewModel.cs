using System.Collections.ObjectModel;
using System.Globalization;
using Cinedex.App.Security;
using Cinedex.App.Services;
using Cinedex.Core.Titles;
using Cinedex.Data;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cinedex.App.ViewModels;

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

    public SettingsViewModel(ISecretStore secrets, SettingsStore settings, string? dataRoot = null)
    {
        _secrets = secrets;
        _settings = settings;
        _dataRoot = dataRoot;

        // Général : apparence commune à tous les catalogues (si l'emplacement des données est connu).
        ThemeChoices = [new(AppTheme.System, "Comme le système"), new(AppTheme.Light, "Clair"), new(AppTheme.Dark, "Sombre")];
        SelectedTheme = ThemeChoices.Single(c => c.Value == (dataRoot is null ? AppTheme.System : Appearance.Load(dataRoot)));

        // Bandeau gauche.
        var display = DisplaySettings.Load(settings);
        MainFilterToggles = [.. MainWindowViewModel.AllMainFilters.Where(f => f.Value != QuickFilter.All)
            .Select(f => new FilterToggle(f.Value, f.Label, !display.HiddenFilters.Contains(f.Value)))];
        DefinitionFilterToggles = [.. MainWindowViewModel.AllDefinitionFilters
            .Select(f => new FilterToggle(f.Value, f.Label, !display.HiddenFilters.Contains(f.Value)))];
        ShowRuleSearch = display.ShowRuleSearch;
        ShowPeople = display.ShowPeople;
        HideAdult = display.HideAdult;

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
    }

    public IReadOnlyList<Choice<AppTheme>> ThemeChoices { get; }

    [ObservableProperty]
    public partial Choice<AppTheme> SelectedTheme { get; set; }

    public bool CanChooseTheme => _dataRoot is not null;

    public string DataLocation => _dataRoot ?? "(inconnu)";

    public ObservableCollection<FilterToggle> MainFilterToggles { get; }

    public ObservableCollection<FilterToggle> DefinitionFilterToggles { get; }

    [ObservableProperty]
    public partial bool ShowRuleSearch { get; set; }

    [ObservableProperty]
    public partial bool ShowPeople { get; set; }

    [ObservableProperty]
    public partial bool HideAdult { get; set; }

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
                Error = $"Chemin du {label} introuvable : {path}";
                return false;
            }
        }

        var maxActors = (int)Math.Round(MaxActorsNumber ?? 0);
        if (maxActors is < 1 or > 200)
        {
            Error = "Nombre d'acteurs : un nombre entre 1 et 200 est attendu.";
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
        }.Save(_settings);
        if (_dataRoot is not null)
        {
            Appearance.Save(_dataRoot, SelectedTheme.Value);
            Appearance.Apply(SelectedTheme.Value);
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
