using System.Collections.ObjectModel;
using System.Globalization;
using Cinedex.App.Services;
using Cinedex.Core.Localization;
using Cinedex.Core.Titles;
using Cinedex.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cinedex.App.ViewModels;

/// <summary>D'où vient la vue demandée par le bandeau : la fenêtre n'y réagit pas tout à fait pareil.</summary>
public enum SideBarSource
{
    /// <summary>Un disque du sous-menu MediaID, choisi ou relâché.</summary>
    Disc,

    /// <summary>Une liste de référence (Top 250, palmarès).</summary>
    ReferenceList,

    /// <summary>Une entrée d'un sous-menu de parcours : genre, décennie, liste personnelle, pays, langue.</summary>
    Facet,
}

/// <summary>Ce que le bandeau demande d'afficher : la recherche à appliquer, et la liste personnelle ouverte s'il y en a une.</summary>
public sealed record SideBarRequest(SideBarSource Source, RuleSearch? Rules, string? ListName);

/// <summary>
/// Les sous-menus du bandeau gauche : disques (MediaID), listes de référence, et les parcours par genre,
/// décennie, liste personnelle, pays et langue. Un seul d'entre eux est choisi à la fois.
/// <para>
/// Sorti de la fenêtre principale. Le bandeau tient ses listes, leur état déplié et leur exclusion
/// mutuelle ; il ne sait rien de la liste des titres. Quand une entrée est choisie il le dit par
/// <see cref="Requested"/>, et la fenêtre applique la recherche.
/// </para>
/// </summary>
public sealed partial class SideBarViewModel : ObservableObject
{
    private readonly TitleRepository _titles;
    private readonly ReferenceListService? _lists;

    /// <summary>Vrai pendant que le bandeau range lui-même ses sélections : ces changements-là ne sont pas des demandes.</summary>
    private bool _quiet;

    public SideBarViewModel(TitleRepository titles, ReferenceListService? lists)
    {
        ArgumentNullException.ThrowIfNull(titles);
        _titles = titles;
        _lists = lists;
    }

    /// <summary>Une entrée du bandeau vient d'être choisie (ou un disque relâché) par l'utilisateur.</summary>
    public event EventHandler<SideBarRequest>? Requested;

    /// <summary>
    /// Bandeau réduit à une colonne d'icônes : la fenêtre le fait d'elle-même quand elle devient étroite, et
    /// F8 le bascule à la main. Les sous-menus, qui sont des listes de texte, attendent qu'on le déplie.
    /// </summary>
    [ObservableProperty]
    public partial bool IsRail { get; set; }

    [RelayCommand]
    private void ToggleRail() => IsRail = !IsRail;

    /// <summary>Disques du catalogue (MediaID), par ordre alphabétique.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMediaIds))]
    public partial IReadOnlyList<string> MediaIds { get; private set; } = [];

    public bool HasMediaIds => MediaIds.Count > 0;

    [ObservableProperty]
    public partial bool IsMediaIdExpanded { get; set; }

    [RelayCommand]
    private void ToggleMediaIds() => IsMediaIdExpanded = !IsMediaIdExpanded;

    /// <summary>Disque choisi : la liste ne montre que ses titres. Le reprendre, ou un filtre, revient à tout.</summary>
    [ObservableProperty]
    public partial string? SelectedMediaId { get; set; }

    partial void OnSelectedMediaIdChanged(string? value)
    {
        if (_quiet)
        {
            return;
        }

        if (value is not null)
        {
            Quietly(ClearFacets);
        }

        // Un disque et une recherche multicritère ne se cumulent pas : le disque remplace les critères, et le
        // relâcher les efface.
        Requested?.Invoke(this, new SideBarRequest(
            SideBarSource.Disc,
            value is null ? null : new RuleSearch([new SearchRule("media_id", RuleOperator.Equals, value)]),
            null));
    }

    /// <summary>
    /// Sous-menus de parcours. Ils se comportent tous pareil, d'où un seul mécanisme : une liste d'entrées,
    /// chacune portant la recherche qu'elle applique.
    /// </summary>
    public ObservableCollection<FacetGroup> Facets { get; } = [];

    /// <summary>Liste personnelle ouverte, s'il y en a une : c'est elle qui donne l'ordre et qu'on réordonne.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPersonalList))]
    public partial string? SelectedListName { get; private set; }

    public bool HasPersonalList => SelectedListName is not null;

    /// <summary>Listes personnelles où verser la sélection ; le menu du clic droit en fait un sous-menu.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLists))]
    public partial IReadOnlyList<string> ListNames { get; private set; } = [];

    public bool HasLists => ListNames.Count > 0;

    /// <summary>Listes de référence (Top 250, palmarès, listes à vous), avec leur état face au catalogue.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReferenceLists))]
    public partial IReadOnlyList<ReferenceList> ReferenceLists { get; private set; } = [];

    public bool HasReferenceLists => ReferenceLists.Count > 0;

    [ObservableProperty]
    public partial bool IsListExpanded { get; set; }

    [RelayCommand]
    private void ToggleLists() => IsListExpanded = !IsListExpanded;

    /// <summary>Liste choisie : le panneau central montre les titres du catalogue qui en font partie.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedList), nameof(ListStatusText))]
    public partial ReferenceList? SelectedReferenceList { get; set; }

    public bool HasSelectedList => SelectedReferenceList is not null;

    /// <summary>Ce que la liste dit du catalogue : c'est elle le sujet, pas le classement lui-même.</summary>
    public string ListStatusText => SelectedReferenceList is not { } list
        ? string.Empty
        : Tr.F("{0} sur {1} au catalogue, {2} manquant(s)", list.Owned, list.Total, list.Missing);

    partial void OnSelectedReferenceListChanged(ReferenceList? value)
    {
        if (_quiet || value is null)
        {
            return;
        }

        Quietly(() =>
        {
            SelectedMediaId = null;
            ClearFacets();
        });
        Requested?.Invoke(this, new SideBarRequest(
            SideBarSource.ReferenceList,
            new RuleSearch([new SearchRule("list", RuleOperator.Equals, value.Name)]),
            null));
    }

    /// <summary>
    /// Relit les listes du bandeau. Elles ne bougent qu'après un import, une mise à jour TMDb ou une
    /// opération de maintenance : c'est à la fenêtre de dire quand. Ce qui était choisi le reste, si
    /// l'entrée existe encore.
    /// </summary>
    public void Reload()
    {
        Quietly(() =>
        {
            var disc = SelectedMediaId;
            MediaIds = _titles.MediaIds();
            if (disc is not null)
            {
                // La sélection suit le disque s'il est toujours là ; sinon elle est relâchée.
                SelectedMediaId = MediaIds.FirstOrDefault(d => string.Equals(d, disc, StringComparison.Ordinal));
            }

            LoadFacets();
            if (_lists is { } service)
            {
                var chosen = SelectedReferenceList?.Id;
                ReferenceLists = service.Lists.All();
                SelectedReferenceList = chosen is { } id ? ReferenceLists.FirstOrDefault(l => l.Id == id) : null;
            }

            ListNames = _titles.Groups().Select(g => g.Name).ToList();
        });
    }

    /// <summary>Choisit une liste de référence par son identifiant, comme si on l'avait cliquée, et déplie son sous-menu.</summary>
    public void ShowReferenceList(long listId)
    {
        IsListExpanded = true;
        SelectedReferenceList = ReferenceLists.FirstOrDefault(l => l.Id == listId);
    }

    /// <summary>
    /// Relâche le disque choisi, sans le signaler. Renvoie vrai s'il y en avait un : sa recherche est alors à
    /// effacer par celui qui relâche.
    /// </summary>
    public bool ReleaseDisc()
    {
        if (SelectedMediaId is null)
        {
            return false;
        }

        Quietly(() => SelectedMediaId = null);
        return true;
    }

    /// <summary>Relâche les sous-menus de parcours et la liste de référence, sans le signaler : une autre vue prend la main.</summary>
    public void ReleaseBrowsing() => Quietly(() =>
    {
        ClearFacets();
        SelectedReferenceList = null;
    });

    /// <summary>Relâche tout ce que le bandeau avait de choisi, sans le signaler.</summary>
    public void ReleaseAll()
    {
        ReleaseDisc();
        ReleaseBrowsing();
    }

    private void ClearFacets()
    {
        SelectedListName = null;
        foreach (var group in Facets)
        {
            group.Clear();
        }
    }

    private void Quietly(Action action)
    {
        var before = _quiet;
        _quiet = true;
        try
        {
            action();
        }
        finally
        {
            _quiet = before;
        }
    }

    private void LoadFacets()
    {
        var chosen = Facets.Select(f => f.SelectedItem?.Label).ToList();
        Facets.Clear();

        var genres = _titles.UsedGenres()
            .Select(g => new FacetItem(Tr.T(g.Label), new RuleSearch([new SearchRule("genre", RuleOperator.Equals, g.Code)])))
            .OrderBy(i => i.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        Add("Genres", "tag", genres);

        Add("Décennies", "clock", _titles.Decades()
            .Select(d => new FacetItem(
                TitleText.DecadeLabel(d),
                new RuleSearch([
                    new SearchRule("year", RuleOperator.GreaterOrEqual, d.ToString(CultureInfo.InvariantCulture)),
                    new SearchRule("year", RuleOperator.LessOrEqual, (d + 9).ToString(CultureInfo.InvariantCulture)),
                ])))
            .ToList());

        // Les groupes sont les listes personnelles : ils existaient, mais seule la recherche multicritère
        // y menait. Les mettre ici est ce qui manquait pour qu'ils servent.
        Add("Mes listes", "bookmark", _titles.Groups()
            .Select(g => new FacetItem(g.Name, new RuleSearch([new SearchRule("group", RuleOperator.Equals, g.Name)]), g.Name))
            .OrderBy(i => i.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToList());

        Add("Pays", "globe", Named(_titles.CountryCodes(), TitleText.CountryName, "countries"));
        Add("Langues", "speech", Named(_titles.LanguageCodes(), TitleText.LanguageName, "languages"));

        // La sélection d'avant survit au rechargement quand son entrée existe encore.
        for (var i = 0; i < Facets.Count && i < chosen.Count; i++)
        {
            if (chosen[i] is { } label)
            {
                Facets[i].SelectedItem = Facets[i].Items.FirstOrDefault(x => string.Equals(x.Label, label, StringComparison.Ordinal));
            }
        }

        void Add(string label, string icon, IReadOnlyList<FacetItem> items)
        {
            if (items.Count > 0)
            {
                Facets.Add(new FacetGroup(Tr.T(label), icon, items, OnFacetSelected));
            }
        }

        static List<FacetItem> Named(IReadOnlyList<string> codes, Func<string, string> name, string field) =>
            codes.Select(c => new FacetItem(name(c), new RuleSearch([new SearchRule(field, RuleOperator.Contains, c)])))
                .OrderBy(i => i.Label, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
    }

    /// <summary>Une entrée de sous-menu cliquée : elle remplace toute autre vue latérale.</summary>
    private void OnFacetSelected(FacetGroup group)
    {
        if (_quiet)
        {
            return;
        }

        Quietly(() =>
        {
            foreach (var other in Facets.Where(f => f != group))
            {
                other.Clear();
            }

            SelectedMediaId = null;
            SelectedReferenceList = null;
        });

        SelectedListName = group.SelectedItem?.GroupName;
        Requested?.Invoke(this, new SideBarRequest(SideBarSource.Facet, group.SelectedItem?.Search, SelectedListName));
    }
}
