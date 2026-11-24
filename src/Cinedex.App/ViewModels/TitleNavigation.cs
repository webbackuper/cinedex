namespace Cinedex.App.ViewModels;

/// <summary>
/// D'où vient la fiche ouverte, et ce que Précédent / Suivant y parcourent : la liste principale dans son tri
/// et son filtre, une bande de l'Accueil, une collection, la filmographie d'une personne, « Vous aimerez
/// aussi »… La séquence est figée à l'ouverture : marquer un film vu dans « Pas encore vus » le retire de la
/// liste, pas de la séquence, et Suivant continue là où l'on en était. Avant, Précédent / Suivant suivaient
/// toujours la liste principale, et l'on tombait sur le voisin alphabétique d'un film ouvert depuis l'Accueil.
/// </summary>
/// <param name="Label">Nom de la vue d'origine, affiché à côté du compteur (« Accueil › Derniers ajouts »).</param>
/// <param name="Ids">Les titres de la vue, dans son ordre.</param>
/// <param name="Index">Position du titre ouvert dans <paramref name="Ids"/>.</param>
public sealed record TitleNavigation(string Label, IReadOnlyList<long> Ids, int Index)
{
    /// <summary>Séparateur des niveaux d'un libellé : « Accueil › Derniers ajouts ».</summary>
    public const string Separator = " › ";

    /// <summary>Le titre ouvert.</summary>
    public long Current => Ids[Index];

    /// <summary>« 3 / 12 · Accueil › Derniers ajouts ».</summary>
    public string CounterText => $"{Index + 1} / {Ids.Count} · {Label}";

    /// <summary>
    /// La séquence d'une vue, placée sur ce titre. Un titre absent de la vue (ouvert d'ailleurs) donne une
    /// séquence d'un seul titre : il n'y a alors ni précédent ni suivant, plutôt qu'un voisin pris au hasard.
    /// </summary>
    public static TitleNavigation For(string label, IEnumerable<long> ids, long current)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var list = ids.Distinct().ToList();
        var index = list.IndexOf(current);
        return index >= 0 ? new TitleNavigation(label, list, index) : new TitleNavigation(label, [current], 0);
    }

    /// <summary>
    /// Le titre voisin dans ce sens, en sautant ceux qui n'existent plus (supprimés depuis l'ouverture) ; nul
    /// au bout de la séquence : on ne reboucle pas.
    /// </summary>
    public TitleNavigation? Step(int direction, Func<long, bool>? exists = null)
    {
        if (direction == 0)
        {
            return null;
        }

        var step = Math.Sign(direction);
        for (var i = Index + step; i >= 0 && i < Ids.Count; i += step)
        {
            if (exists is null || exists(Ids[i]))
            {
                return this with { Index = i };
            }
        }

        return null;
    }
}
