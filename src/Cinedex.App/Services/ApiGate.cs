using System.Collections.Concurrent;

namespace Cinedex.App.Services;

public enum ExternalApi
{
    Tmdb,
    Omdb,
    ImdbDataset,
}

/// <summary>Décision de l'utilisateur quand une API atteint sa limite pendant un import.</summary>
public interface IApiLimitPrompt
{
    /// <summary>Vrai : continuer sans cette API ; faux : interrompre l'opération en cours.</summary>
    Task<bool> ContinueWithoutAsync(ExternalApi api, string reason, string consequence);
}

/// <summary>Opération interrompue par l'utilisateur après une limite d'API.</summary>
public sealed class ApiLimitAbortedException(ExternalApi api, string reason)
    : OperationCanceledException($"Opération interrompue : {reason}")
{
    public ExternalApi Api { get; } = api;
}

/// <summary>
/// Point unique de décision quand une API (TMDb, OMDb, fichier IMDb) atteint sa limite ou refuse la clé :
/// l'utilisateur choisit une fois, pour la session, de continuer sans cette API ou d'interrompre.
/// Les services consultent <see cref="IsDisabled"/> avant chaque appel.
/// </summary>
public sealed class ApiGate(IApiLimitPrompt? prompt = null) : IDisposable
{
    private static readonly Dictionary<ExternalApi, string> Consequences = new Dictionary<ExternalApi, string>
    {
        [ExternalApi.Tmdb] = "Les titres restants seront traités sans TMDb : lors d'un scan, ils sont ajoutés à partir du nom de fichier (et du .nfo) ; lors d'une mise à jour, ils restent en l'état mais leurs notes sont complétées si possible. Relancez « Mettre à jour via TMDb » plus tard.",
        [ExternalApi.Omdb] = "Les notes Rotten Tomatoes et Metacritic ne seront pas récupérées pour les titres restants ; la note IMDb viendra du fichier officiel IMDb.",
        [ExternalApi.ImdbDataset] = "Les notes IMDb ne seront pas complétées pour les titres restants.",
    };

    private readonly ConcurrentDictionary<ExternalApi, string> _disabled = new();
    private readonly SemaphoreSlim _asking = new(1, 1);

    public bool IsDisabled(ExternalApi api) => _disabled.ContainsKey(api);

    /// <summary>Raisons des API désactivées pendant la session, pour le bilan.</summary>
    public IReadOnlyDictionary<ExternalApi, string> Disabled => _disabled;

    /// <summary>
    /// Signale une limite atteinte. Demande à l'utilisateur la première fois ; s'il interrompt, lève
    /// <see cref="ApiLimitAbortedException"/>. Sans fenêtre (tests, tâches de fond), on continue sans l'API.
    /// </summary>
    public async Task ReportLimitAsync(ExternalApi api, string reason)
    {
        if (IsDisabled(api))
        {
            return;
        }

        await _asking.WaitAsync().ConfigureAwait(false);
        try
        {
            if (IsDisabled(api))
            {
                return;
            }

            var keepGoing = prompt is null || await prompt.ContinueWithoutAsync(api, reason, Consequences[api]).ConfigureAwait(false);
            if (!keepGoing)
            {
                throw new ApiLimitAbortedException(api, reason);
            }

            _disabled[api] = reason;
        }
        finally
        {
            _asking.Release();
        }
    }

    /// <summary>Réactive toutes les API (nouvelle opération lancée par l'utilisateur, réglages modifiés).</summary>
    public void Reset() => _disabled.Clear();

    public void Dispose() => _asking.Dispose();
}
