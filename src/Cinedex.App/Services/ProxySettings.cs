using System.Globalization;
using System.Net;
using System.Text.Json;

namespace Cinedex.App.Services;

/// <summary>
/// Proxy réseau de Cinedex (TMDb, OMDb, notes IMDb, images) : HTTP ou HTTPS, de la forme <c>serveur:port</c>.
/// Désactivé par défaut : les connexions suivent alors le proxy du système. Réglage commun à tous les catalogues,
/// mémorisé dans <c>proxy.json</c> à la racine des données.
/// </summary>
public sealed record ProxySettings
{
    private const string FileName = "proxy.json";

    /// <summary>Réglage en vigueur, lu au démarrage et remplacé à l'enregistrement des réglages.</summary>
    public static ProxySettings Current { get; set; } = new();

    public bool Enabled { get; init; }

    /// <summary>Proxy joint en HTTPS (connexion chiffrée jusqu'au proxy) plutôt qu'en HTTP.</summary>
    public bool UseHttps { get; init; }

    /// <summary>Adresse du proxy, de la forme <c>myserver.proxy.com:8080</c>.</summary>
    public string Address { get; init; } = string.Empty;

    /// <summary>Adresse complète du proxy, ou null si elle n'est pas de la forme serveur:port.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Uri? ProxyUri => TryParse(Address, UseHttps, out var uri) ? uri : null;

    /// <summary>
    /// Lit « serveur:port » (un éventuel http:// ou https:// saisi est ignoré : le type se choisit à part).
    /// Le port doit être compris entre 1 et 65535.
    /// </summary>
    public static bool TryParse(string? address, bool https, out Uri? uri)
    {
        uri = null;
        var text = (address ?? string.Empty).Trim().TrimEnd('/');
        foreach (var prefix in new[] { "http://", "https://" })
        {
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                text = text[prefix.Length..];
            }
        }

        var colon = text.LastIndexOf(':');
        if (colon <= 0 || colon == text.Length - 1 || text.Contains('/') || text.Contains(' ') || text.Contains('@'))
        {
            return false;
        }

        var host = text[..colon];
        if (!int.TryParse(text[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535 ||
            Uri.CheckHostName(host.Trim('[', ']')) == UriHostNameType.Unknown)
        {
            return false;
        }

        return Uri.TryCreate($"{(https ? "https" : "http")}://{host}:{port.ToString(CultureInfo.InvariantCulture)}", UriKind.Absolute, out uri);
    }

    public static ProxySettings Load(string dataRoot)
    {
        try
        {
            var path = Path.Combine(dataRoot, FileName);
            return File.Exists(path) ? JsonSerializer.Deserialize<ProxySettings>(File.ReadAllText(path)) ?? new ProxySettings() : new ProxySettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new ProxySettings();
        }
    }

    public void Save(string dataRoot)
    {
        Directory.CreateDirectory(dataRoot);
        File.WriteAllText(Path.Combine(dataRoot, FileName), JsonSerializer.Serialize(this));
    }
}

/// <summary>
/// Proxy relu à chaque requête : un changement dans les réglages s'applique sans redémarrer. Désactivé (ou mal
/// renseigné), il laisse la main au proxy du système.
/// </summary>
public sealed class SwitchableProxy(Func<ProxySettings> settings) : IWebProxy
{
    public ICredentials? Credentials { get; set; } = CredentialCache.DefaultCredentials;

    private IWebProxy? Configured => settings() is { Enabled: true, ProxyUri: { } uri } ? new WebProxy(uri) : null;

    public Uri? GetProxy(Uri destination) => Configured is { } proxy
        ? proxy.GetProxy(destination)
        : HttpClient.DefaultProxy.GetProxy(destination);

    public bool IsBypassed(Uri host) => Configured is { } proxy
        ? proxy.IsBypassed(host)
        : HttpClient.DefaultProxy.IsBypassed(host);
}
