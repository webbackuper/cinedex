namespace Cinedex.App.Services;

/// <summary>
/// Client HTTP d'un coffret : il n'appelle jamais le réseau. Tout ce qui passerait par Internet (TMDb, OMDb,
/// notes IMDb, photos) échoue tout de suite comme une absence de connexion, que l'application sait déjà
/// traiter : la fiche reste ce que le disque contient.
/// </summary>
public sealed class OfflineHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromException<HttpResponseMessage>(new HttpRequestException("Coffret Cinedex : aucun accès au réseau."));
}
