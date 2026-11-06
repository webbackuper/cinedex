using System.Collections.ObjectModel;
using Cinedex.Core.Localization;
using Cinedex.Core.Titles;
using Cinedex.Data;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cinedex.App.ViewModels;

/// <summary>Une bande de la page d'accueil : son titre, sa phrase d'explication et ses films.</summary>
public sealed record HomeBand(string Title, string Hint, IReadOnlyList<TitleSummary> Titles)
{
    public bool HasTitles => Titles.Count > 0;
}

/// <summary>
/// Page d'accueil : ce qu'on peut regarder ce soir, les sagas commencées, les derniers ajouts et les films
/// oubliés. Elle remplace l'ouverture sur 5 000 affiches par ordre alphabétique, qui est un inventaire et
/// ne répond à aucune question.
/// </summary>
public sealed partial class HomeViewModel : ObservableObject
{
    public ObservableCollection<HomeBand> Bands { get; } = [];

    public bool HasBands => Bands.Count > 0;

    /// <summary>Une ligne de chiffres sobres : les statistiques existaient, mais enterrées dans les réglages.</summary>
    [ObservableProperty]
    public partial string Summary { get; private set; } = string.Empty;

    /// <summary>
    /// Remplit les bandes. <paramref name="prepare"/> transforme les identifiants en fiches affichables, en
    /// gardant l'ordre voulu par la requête.
    /// </summary>
    public void Load(HomeRepository home, Func<IReadOnlyList<long>, IReadOnlyList<TitleSummary>> prepare, IReadOnlyCollection<string> mountedLabels, CatalogStatistics? statistics)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentNullException.ThrowIfNull(prepare);
        var day = DateOnly.FromDateTime(DateTime.Now);

        Bands.Clear();
        Add("À voir ce soir", "Jamais vus, bien notés, et le fichier est lisible maintenant.", home.Tonight(mountedLabels, day));
        Add("Reprendre une saga", "Vous avez vu un film de la saga, pas celui-ci.", home.ResumeSaga());
        Add("Derniers ajouts", "Entrés récemment au catalogue.", home.JustAdded());
        Add("Oubliés", "Au catalogue depuis plus de six mois, jamais vus.", home.Forgotten(day));
        OnPropertyChanged(nameof(HasBands));

        Summary = statistics is not { } s ? string.Empty : string.Join(" · ", new[]
        {
            Tr.F("{0} films", s.Films),
            s.Series > 0 ? Tr.F("{0} séries", s.Series) : null,
            s.FilmMinutes > 0 ? Tr.F("{0} h de film", s.FilmMinutes / 60) : null,
            Tr.F("{0} vus", s.Seen),
        }.Where(p => p is not null));

        void Add(string title, string hint, IReadOnlyList<long> ids)
        {
            if (ids.Count > 0 && prepare(ids) is { Count: > 0 } titles)
            {
                Bands.Add(new HomeBand(Tr.T(title), Tr.T(hint), titles));
            }
        }
    }
}
