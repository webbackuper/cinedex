using Cinedex.Core.Localization;
using Cinedex.Core.Metadata;
using Cinedex.Core.Titles;
using Cinedex.Data;
using Cinedex.Metadata.Ratings;

namespace Cinedex.App.Services;

/// <summary>
/// Construit les listes de référence et les confronte au catalogue. Le Top 250 est recalculé depuis les
/// fichiers publics d'IMDb : le classement lui-même n'est pas distribué, mais sa formule l'est. La liste
/// produite est donc une estimation, et elle le dit.
/// </summary>
public sealed class ReferenceListService(
    ReferenceListRepository lists,
    ImdbRatingsDataset ratings,
    ImdbTopChart chart,
    TitleRepository titles,
    MetadataService? metadata = null)
{
    public const string ImdbTopName = "Top 250 IMDb (estimé)";

    private const string ImdbTopNote = "Recalculé depuis les fichiers publics d'IMDb avec la formule du classement (moyenne bayésienne, 25 000 votes minimum). IMDb applique en plus des pondérations non publiées : la liste ressemble beaucoup à la vraie sans lui être identique.";

    public ReferenceListRepository Lists => lists;

    /// <summary>
    /// Télécharge ce qu'il faut, recalcule le classement et remplace la liste. Le gros fichier des titres
    /// (environ 200 Mo) n'est repris qu'une fois par semaine : il ne bouge presque pas.
    /// </summary>
    public async Task<ReferenceList> BuildImdbTopAsync(int count = 250, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        await ratings.RefreshAsync(TimeSpan.FromDays(1), cancellationToken).ConfigureAwait(false);
        await chart.RefreshAsync(TimeSpan.FromDays(7), progress, cancellationToken).ConfigureAwait(false);
        progress?.Report(Tr.T("Calcul du classement…"));
        var entries = await chart.ComputeAsync(ratings.CachePath, count, progress, cancellationToken).ConfigureAwait(false);
        lists.Replace(
            ImdbTopName,
            "imdb_top250",
            Tr.T(ImdbTopNote),
            entries.Select(e => new ReferenceEntry(e.Rank, e.ImdbId, null, e.Title, e.Year, e.Rating, e.Votes, null)).ToList());
        return lists.Find(ImdbTopName)!;
    }

    /// <summary>
    /// Ajoute à la liste de souhaits les films de la liste que le catalogue n'a pas : la fiche est rapatriée
    /// de TMDb par l'identifiant IMDb, l'entrée est marquée non possédée et étiquetée au nom de la liste.
    /// Renvoie le nombre de films ajoutés et celui des films que TMDb n'a pas su retrouver.
    /// </summary>
    public async Task<(int Added, int NotFound)> AddMissingToWishlistAsync(
        ReferenceList list, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(list);
        if (metadata is null)
        {
            throw new InvalidOperationException(Tr.T("Aucun accès à TMDb : renseignez le jeton dans les réglages."));
        }

        var missing = lists.Entries(list.Id).Where(e => !e.IsOwned && e.ImdbId is not null).ToList();
        var added = 0;
        var notFound = 0;
        var done = 0;
        foreach (var entry in missing)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(Tr.F("{0} sur {1} : {2}", ++done, missing.Count, entry.Title));
            var found = await metadata.SearchAsync(entry.ImdbId!, cancellationToken).ConfigureAwait(false);
            if (found.FirstOrDefault(r => r.Kind == TitleKind.Movie) is not { } match)
            {
                notFound++;
                continue;
            }

            var id = await metadata.ImportAsync(match, ImportOptions.Default, cancellationToken).ConfigureAwait(false);
            titles.SetOwned([id], false);
            // L'étiquette dit d'où vient le souhait : « Top 250 IMDb (estimé) » et non une envie de passage.
            titles.ApplyBulk([id], new BulkEdit { AddTags = [list.Name] });
            added++;
        }

        return (added, notFound);
    }
}
