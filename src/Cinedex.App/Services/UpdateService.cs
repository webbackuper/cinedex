using System.Text.Json;
using Velopack;
using Velopack.Sources;

namespace Cinedex.App.Services;

/// <summary>Préférence des mises à jour : recherche automatique au démarrage (activée par défaut).</summary>
public sealed record UpdatePreferences
{
    private const string FileName = "updates.json";

    public bool CheckAtStartup { get; init; } = true;

    public static UpdatePreferences Load(string dataRoot)
    {
        try
        {
            var path = Path.Combine(dataRoot, FileName);
            return File.Exists(path) ? JsonSerializer.Deserialize<UpdatePreferences>(File.ReadAllText(path)) ?? new UpdatePreferences() : new UpdatePreferences();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new UpdatePreferences();
        }
    }

    public void Save(string dataRoot)
    {
        Directory.CreateDirectory(dataRoot);
        File.WriteAllText(Path.Combine(dataRoot, FileName), JsonSerializer.Serialize(this));
    }
}

/// <summary>
/// Mises à jour automatiques (Velopack) depuis les versions publiées sur GitHub : recherche, téléchargement
/// (différentiel quand c'est possible), installation et redémarrage. Disponible pour la version installée
/// (Cinedex-win-Setup.exe) ; la version portable (archive .zip) se met à jour à la main.
/// </summary>
public sealed class UpdateService
{
    public const string RepositoryUrl = "https://github.com/webbackuper/cinedex";

    private readonly UpdateManager? _manager;
    private UpdateInfo? _pending;

    public UpdateService()
    {
        try
        {
            _manager = new UpdateManager(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false, new ProxyDownloader()));
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or NotSupportedException or PlatformNotSupportedException)
        {
            _manager = null;
        }
    }

    /// <summary>Installée par le programme d'installation : seules ces copies peuvent se mettre à jour.</summary>
    public bool IsInstalled => _manager?.IsInstalled == true;

    /// <summary>Version disponible au téléchargement (après une recherche), ou null.</summary>
    public string? AvailableVersion => _pending?.TargetFullRelease.Version.ToString();

    /// <summary>Cherche une version plus récente ; renvoie son numéro, ou null s'il n'y en a pas.</summary>
    public async Task<string?> CheckAsync()
    {
        if (_manager is not { IsInstalled: true })
        {
            return null;
        }

        _pending = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
        return AvailableVersion;
    }

    /// <summary>Télécharge la version trouvée (progression de 0 à 100).</summary>
    public async Task DownloadAsync(Action<int>? progress = null, CancellationToken cancellationToken = default)
    {
        if (_manager is null || _pending is null)
        {
            throw new InvalidOperationException(Cinedex.Core.Localization.Tr.T("Aucune mise à jour à télécharger."));
        }

        await _manager.DownloadUpdatesAsync(_pending, progress, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Ferme Cinedex, installe la version téléchargée et le relance.</summary>
    public void ApplyAndRestart()
    {
        if (_manager is not null && _pending is not null)
        {
            _manager.ApplyUpdatesAndRestart(_pending);
        }
    }

    /// <summary>Téléchargements des mises à jour par le même proxy que le reste de l'application (Réglages > Réseau).</summary>
    private sealed class ProxyDownloader : HttpClientFileDownloader
    {
        protected override HttpClientHandler CreateHttpClientHandler()
        {
            var handler = base.CreateHttpClientHandler();
            handler.Proxy = new SwitchableProxy(() => ProxySettings.Current);
            handler.UseProxy = true;
            return handler;
        }
    }
}
