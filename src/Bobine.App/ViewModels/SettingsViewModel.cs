using Bobine.App.Security;
using Bobine.Data;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Bobine.App.ViewModels;

/// <summary>
/// Réglages des sources de données. Le jeton TMDb va dans le coffre du système ; il n'est jamais réaffiché,
/// seule sa présence est indiquée.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    public const string LanguageKey = "tmdb.language";
    public const string CountryKey = "tmdb.country";

    private readonly ISecretStore _secrets;
    private readonly SettingsStore _settings;

    public SettingsViewModel(ISecretStore secrets, SettingsStore settings)
    {
        _secrets = secrets;
        _settings = settings;
        Language = settings.Get(LanguageKey) ?? "fr-FR";
        Country = settings.Get(CountryKey) ?? "FR";
        FfprobePath = settings.Get(Services.MediaContext.FfprobeKey) ?? string.Empty;
        MediaInfoPath = settings.Get(Services.MediaContext.MediaInfoKey) ?? string.Empty;
        PlayerPath = settings.Get(Services.MediaContext.PlayerKey) ?? string.Empty;
        PlayerArgs = settings.Get(Services.MediaContext.PlayerArgsKey) ?? string.Empty;
        MarkSeenOnPlay = settings.Get(Services.MediaContext.MarkSeenOnPlayKey) != "false";
        IgnoreBracketed = settings.Get(Services.MediaContext.IgnoreBracketedKey) != "false";
        var nfo = Services.NfoSettings.Load(settings);
        NfoReadOnScan = nfo.ReadOnScan;
        NfoWriteAfterMetadata = nfo.WriteAfterMetadata;
        NfoOverwrite = nfo.OverwriteExisting;
        NfoBackup = nfo.Backup;
        HasToken = SafeRead() is not null;
    }

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
        _settings.Set(Services.MediaContext.IgnoreBracketedKey, IgnoreBracketed ? "true" : "false");
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

    private string? SafeRead()
    {
        try
        {
            return _secrets.Read(SecretStore.TmdbToken);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
