using Cinedex.Core.People;
using Cinedex.Data;
using Cinedex.Metadata.Tmdb;
using Microsoft.Extensions.Logging;

namespace Cinedex.App.Services;

/// <summary>
/// Fiches personnes : la biographie et la photo sont récupérées sur TMDb à la première ouverture d'une fiche
/// (puis gardées dans la base), et les photos de la distribution d'un film à l'affichage de sa fiche.
/// </summary>
public sealed class PeopleService(PeopleRepository people, TmdbClient tmdb, ImageStore images, ILogger<PeopleService> logger)
{
    public PeopleRepository Repository => people;

    public string? ResolvePhoto(string? relativePath) => relativePath is null ? null : images.ResolveInside(relativePath);

    /// <summary>Complète la fiche depuis TMDb si elle ne l'a jamais été ; renvoie la fiche à jour.</summary>
    public async Task<PersonDetail?> EnsureDetailsAsync(long personId, CancellationToken cancellationToken = default)
    {
        var person = people.Get(personId);
        if (person is null)
        {
            return null;
        }

        if (!person.HasDetails && person.TmdbId is { } tmdbId)
        {
            try
            {
                var remote = await tmdb.GetPersonAsync(tmdbId, cancellationToken).ConfigureAwait(false);
                people.SaveDetails(personId, remote);
            }
            catch (Exception ex) when (ex is TmdbAuthenticationException or TmdbUnavailableException or HttpRequestException or KeyNotFoundException)
            {
                logger.LogWarning("Fiche TMDb de la personne {PersonId} indisponible : {Message}", personId, ex.Message);
            }
        }

        await EnsurePhotoAsync(people.Get(personId)!, "h632", cancellationToken).ConfigureAwait(false);
        return Resolve(people.Get(personId)!);
    }

    /// <summary>
    /// Télécharge depuis TMDb la fiche (biographie, dates, photo) des personnes qui ne l'ont pas encore ;
    /// renvoie le nombre de fiches complétées.
    /// </summary>
    public async Task<int> EnsureAllDetailsAsync(IReadOnlyList<long> personIds, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(personIds);
        var done = 0;
        for (var i = 0; i < personIds.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (people.Get(personIds[i]) is not { } person || (person.HasDetails && person.PhotoRelativePath is not null) || person.TmdbId is null)
            {
                continue;
            }

            progress?.Report(Cinedex.Core.Localization.Tr.F("Fiches TMDb : {0} / {1} — {2}", i + 1, personIds.Count, person.Name));
            var after = await EnsureDetailsAsync(person.Id, cancellationToken).ConfigureAwait(false);
            if (after is { HasDetails: true })
            {
                done++;
            }
        }

        return done;
    }

    /// <summary>Télécharge les photos manquantes des premières personnes d'un générique ; renvoie le nombre ajouté.</summary>
    public async Task<int> EnsureCreditPhotosAsync(IEnumerable<CastMember> credits, int max = 12, CancellationToken cancellationToken = default)
    {
        var added = 0;
        foreach (var member in credits.Where(c => c.PhotoRelativePath is null && c.ProfilePath is not null).Take(max))
        {
            if (people.Get(member.PersonId) is { } person && await EnsurePhotoAsync(person, "w185", cancellationToken).ConfigureAwait(false))
            {
                added++;
            }
        }

        return added;
    }

    public PersonDetail Resolve(PersonDetail person) => person with { PhotoPath = ResolvePhoto(person.PhotoRelativePath) };

    private async Task<bool> EnsurePhotoAsync(PersonDetail person, string size, CancellationToken cancellationToken)
    {
        if (person.PhotoRelativePath is not null && ResolvePhoto(person.PhotoRelativePath) is not null)
        {
            return false;
        }

        if (person.ProfilePath is not { } path)
        {
            return false;
        }

        try
        {
            var bytes = await tmdb.DownloadImageAsync(path, size, cancellationToken).ConfigureAwait(false);
            people.SetPhoto(person.Id, images.Save(bytes, $"https://{TmdbClient.ImageHost}/t/p/{size}{path}"));
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or TmdbUnavailableException or ArgumentException)
        {
            logger.LogWarning("Photo de {Name} non téléchargée : {Message}", person.Name, ex.Message);
            return false;
        }
    }
}
