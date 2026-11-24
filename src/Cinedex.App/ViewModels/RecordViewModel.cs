using System.Collections.ObjectModel;
using Cinedex.App.Services;
using Cinedex.Core.Localization;
using Cinedex.Core.People;
using Cinedex.Core.Titles;
using Cinedex.Data;
using Cinedex.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cinedex.App.ViewModels;

/// <summary>
/// La fiche de droite : le titre affiché, et tout ce qu'on lit de lui pour le montrer — affiche, décor,
/// fichiers, genres, générique, films proches, suggestions.
/// <para>
/// Sortie de la fenêtre principale, qui ne lui dit plus que « montre ce titre ». Les actions sur le titre
/// (modifier, lire, prêter) restent à la fenêtre : elles touchent à la liste et aux dialogues, pas à ce
/// qu'on lit ici.
/// </para>
/// </summary>
public sealed partial class RecordViewModel : ObservableObject
{
    /// <summary>Délai d'immobilité avant de charger le complément : tenir la flèche bas ne le déclenche pas.</summary>
    private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(80);

    private readonly TitleRepository _titles;
    private readonly ImageStore? _images;
    private readonly MediaContext? _media;
    private readonly CatalogExtras _extras;
    private readonly Func<TitleSummary, TitleSummary> _prepare;

    private Avalonia.Threading.DispatcherTimer? _timer;
    private int _generation;

    /// <summary>Titre dont le complément de fiche est affiché ; nul tant qu'il n'est pas chargé.</summary>
    private long? _loadedFor;

    /// <param name="prepare">Rend une ligne de liste prête à afficher (chemin de l'affiche, sous-titre) : la même mise en forme que la liste.</param>
    public RecordViewModel(TitleRepository titles, ImageStore? images, MediaContext? media, CatalogExtras extras, Func<TitleSummary, TitleSummary> prepare)
    {
        ArgumentNullException.ThrowIfNull(titles);
        ArgumentNullException.ThrowIfNull(extras);
        ArgumentNullException.ThrowIfNull(prepare);
        _titles = titles;
        _images = images;
        _media = media;
        _extras = extras;
        _prepare = prepare;
        Presence = new FilePresenceViewModel(
            media is null ? null : checkedFiles => media.Files.RecordPresence(checkedFiles),
            delay: Avalonia.Application.Current is null ? TimeSpan.Zero : Delay);
        Presence.Checked += (_, _) => OnPresenceChecked();
    }

    /// <summary>Pastille « Présent / Absent » à côté du bouton Lire, vérifiée sur le disque à chaque titre affiché.</summary>
    public FilePresenceViewModel Presence { get; }

    /// <summary>Revérifie la présence des fichiers du titre affiché (après une lecture, par exemple).</summary>
    public Task CheckPresenceAsync() => Presence.CheckAsync(Detail?.Id, DetailFiles);

    /// <summary>
    /// La vérification ne dit pas la même chose que ce qu'on montre (un fichier marqué « introuvable » est
    /// revenu, ou l'inverse) : le « (introuvable) » du résumé technique et la liste des fichiers sont relus,
    /// sans relancer de vérification. On compare à ce qui est affiché plutôt qu'au nombre de lignes changées
    /// en base : une vérification dépassée a pu écrire en base juste avant celle-ci.
    /// </summary>
    private void OnPresenceChecked()
    {
        if (_media is null || Detail is not { } detail || Presence.TitleId != detail.Id)
        {
            return;
        }

        var shown = DetailFiles.ToDictionary(f => f.Id, f => f.IsMissing);
        var differs = Presence.Results.Any(r => r.Check != FileCheck.TimedOut &&
            (!shown.TryGetValue(r.File.Id, out var missing) || missing != (r.Check == FileCheck.Absent)));
        if (!differs)
        {
            return;
        }

        DetailFiles = _media.Files.FilesOf(detail.Id);
        if (_loadedFor == detail.Id)
        {
            TechnicalSummary = MediaTextOf(detail.Id);
        }
    }

    /// <summary>Le titre affiché ; nul quand la sélection est vide ou multiple.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetail), nameof(IsSeries), nameof(HasTrailer))]
    public partial TitleDetail? Detail { get; private set; }

    public bool HasDetail => Detail is not null;

    public bool IsSeries => Detail?.Kind == TitleKind.Series;

    public bool HasTrailer => !string.IsNullOrWhiteSpace(Detail?.TrailerUrl);

    /// <summary>Nom de l'emprunteur à saisir pour un prêt ; prérempli avec le dernier quand la fiche est libre.</summary>
    [ObservableProperty]
    public partial string BorrowerText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? PosterPath { get; private set; }

    [ObservableProperty]
    public partial string? BackdropPath { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetailFiles))]
    public partial IReadOnlyList<MediaFileEntry> DetailFiles { get; private set; } = [];

    public bool HasDetailFiles => DetailFiles.Count > 0;

    [ObservableProperty]
    public partial string? TechnicalSummary { get; private set; }

    /// <summary>Genres du titre affiché, dans la langue de l'interface (pastilles sous les boutons de la fiche).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetailGenres))]
    public partial IReadOnlyList<string> DetailGenres { get; private set; } = [];

    public bool HasDetailGenres => DetailGenres.Count > 0;

    /// <summary>
    /// « Vous aimerez aussi » : titres du catalogue proches de la fiche affichée, chacun avec la raison du
    /// rapprochement. Calculé sur la base, donc hors ligne ; vide quand rien ne ressort.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSimilar))]
    public partial IReadOnlyList<SimilarTitle> Similar { get; private set; } = [];

    public bool HasSimilar => Similar.Count > 0;

    /// <summary>
    /// Suggestions de TMDb pour la fiche affichée. Elles portent surtout sur des films que vous n'avez pas :
    /// elles complètent « Vous aimerez aussi » plutôt que de le remplacer. Récupérées à la demande, puis
    /// gardées en base : ouvrir une fiche ne coûte jamais d'appel réseau.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTmdbSuggestions))]
    public partial IReadOnlyList<TmdbSuggestion> TmdbSuggestions { get; private set; } = [];

    public bool HasTmdbSuggestions => TmdbSuggestions.Count > 0;

    [ObservableProperty]
    public partial bool SuggestionsFetched { get; private set; }

    /// <summary>Distribution (acteurs, avec photo et personnage) du titre affiché.</summary>
    public ObservableCollection<CastMember> Cast { get; } = [];

    /// <summary>Réalisateurs du titre affiché, cliquables (fiche et films réalisés).</summary>
    public ObservableCollection<CastMember> Directors { get; } = [];

    [ObservableProperty]
    public partial string? CrewText { get; private set; }

    public bool HasCast => Cast.Count > 0;

    public bool HasDirectors => Directors.Count > 0;

    /// <summary>
    /// Personnes du titre affiché, pour rebondir sur leurs autres films : réalisateurs d'abord, puis les
    /// premiers rôles. Chaque entrée porte la recherche qu'un clic applique.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectionPeople))]
    public partial IReadOnlyList<FacetItem> SelectionPeople { get; private set; } = [];

    public bool HasSelectionPeople => SelectionPeople.Count > 0;

    /// <summary>
    /// Montre ce titre, ou rien. Les champs de la fiche, l'affiche, les fichiers et les genres sont là tout
    /// de suite ; le complément suit (voir <see cref="ScheduleExtras"/>).
    /// </summary>
    public void Show(long? titleId)
    {
        Detail = titleId is { } id ? _titles.Get(id) : null;
        PosterPath = Detail is null ? null : _images?.PathFor(Detail.Id, "poster");
        BackdropPath = Detail is null ? null : _images?.PathFor(Detail.Id, "backdrop");
        DetailFiles = Detail is null || _media is null ? [] : _media.Files.FilesOf(Detail.Id);
        DetailGenres = Detail is null ? [] : _titles.GenreLabels(Detail.Id).Select(Tr.T).ToList();
        ScheduleExtras();

        // Le même titre republié (après une modification) garde sa pastille ; un autre titre est revérifié.
        if (Detail?.Id != Presence.TitleId || Presence.State == PresenceState.None)
        {
            _ = CheckPresenceAsync();
        }

        // Le dernier emprunteur ne sert qu'à préremplir le prêt d'une fiche affichée et libre : inutile de
        // le demander à la base quand aucune fiche n'est montrée (sélection multiple, liste vide).
        BorrowerText = Detail is { CurrentLoan: null } ? _titles.LastBorrower() ?? string.Empty : string.Empty;
    }

    /// <summary>
    /// Le complément de la fiche est-il là ? Sinon il est lu tout de suite. À appeler avant d'ouvrir ce qui
    /// s'en sert (la fiche d'édition, le menu du clic droit), au cas où l'on agit dans le délai d'attente.
    /// </summary>
    public void EnsureLoaded()
    {
        if (Detail is { } detail && _loadedFor != detail.Id)
        {
            ++_generation;
            _timer?.Stop();
            ApplyExtras(ReadExtras(detail.Id));
        }
    }

    /// <summary>Relit les suggestions TMDb de la fiche affichée, après qu'elles ont été demandées ou qu'une a été ajoutée.</summary>
    public void ReloadSuggestions()
    {
        if (_extras.Suggestions is null || Detail is null)
        {
            TmdbSuggestions = [];
            SuggestionsFetched = false;
            return;
        }

        TmdbSuggestions = _extras.Suggestions.For(Detail.Id);
        SuggestionsFetched = _extras.Suggestions.WasFetched(Detail.Id);
    }

    /// <summary>
    /// Titres proches de celui-ci, dans l'ordre des scores. Public, parce que l'onglet Recommandations de la
    /// fiche ouverte en a besoin pour le titre qu'il édite, qui n'est pas forcément celui affiché ici.
    /// </summary>
    public List<SimilarTitle> SimilarOf(long titleId)
    {
        if (_extras.Recommendations is null)
        {
            return [];
        }

        var found = _extras.Recommendations.For(titleId);
        if (found.Count == 0)
        {
            return [];
        }

        // Les fiches viennent de la requête ordinaire (affiche, année, notes) ; l'ordre reste celui des scores.
        var rows = _titles.Query(new TitleQuery(Ids: found.Select(r => r.TitleId).ToList())).Select(_prepare).ToDictionary(t => t.Id);
        return found.Where(r => rows.ContainsKey(r.TitleId))
            .Select(r => new SimilarTitle(rows[r.TitleId], r.Reason))
            .ToList();
    }

    /// <summary>
    /// Distribution, réalisateurs et équipe d'un titre, photos résolues. Partagé par le panneau de droite et
    /// par la fiche ouverte, qui doivent montrer la même chose après une mise à jour.
    /// </summary>
    public (IReadOnlyList<CastMember> Cast, IReadOnlyList<CastMember> Directors, string Crew) CreditsOf(long titleId)
    {
        if (_media?.People is not { } people)
        {
            return ([], [], string.Empty);
        }

        var credits = people.Repository.Credits(titleId);
        var cast = credits.Where(c => c.Role == "actor")
            .Select(c => c with { PhotoPath = people.ResolvePhoto(c.PhotoRelativePath) })
            .ToList();
        var directors = credits.Where(c => c.Role == "director").GroupBy(c => c.PersonId).Select(g => g.First())
            .Select(c => c with { PhotoPath = people.ResolvePhoto(c.PhotoRelativePath) })
            .ToList();

        // Réalisateurs affichés à part (cliquables) ; le reste de l'équipe en texte.
        var crew = credits.Where(c => c.Role is not ("actor" or "director")).GroupBy(c => c.RoleLabel)
            .Select(g => $"{g.Key} : {string.Join(", ", g.Select(c => c.Name).Distinct())}");
        return (cast, directors, string.Join('\n', crew));
    }

    /// <summary>Emplacement complet et infos MediaInfo de chaque fichier du titre, ou une indication s'il n'y en a pas.</summary>
    public string MediaTextOf(long titleId)
    {
        var files = _media?.Files.TechnicalDetails(titleId) ?? [];
        return files.Count == 0
            ? "Aucun fichier vidéo rattaché à ce titre (ajouté depuis TMDb ou à la main). Utilisez « Importer » pour l'associer à son fichier."
            : string.Join("\n\n", files.Select(f => f.Describe()));
    }

    /// <summary>
    /// Ce que la fiche montre en plus de ses champs, et qui coûte à lire : infos techniques, générique, films
    /// proches, suggestions. Lu d'un bloc pour pouvoir l'être hors du fil d'interface.
    /// </summary>
    private sealed record Extras(
        long TitleId,
        string? Technical,
        IReadOnlyList<SimilarTitle> Similar,
        IReadOnlyList<TmdbSuggestion> Suggestions,
        bool SuggestionsFetched,
        IReadOnlyList<CastMember> Cast,
        IReadOnlyList<CastMember> Directors,
        string Crew,
        IReadOnlyList<CastMember> Credits);

    /// <summary>
    /// Charge le complément de la fiche affichée. Au parcours clavier chaque flèche change de titre : les
    /// lire tous en entier faisait une dizaine de requêtes par touche pour des fiches qu'on ne regarde pas.
    /// Le complément est donc vidé tout de suite, puis lu en arrière-plan après un court arrêt, et abandonné
    /// si la sélection a encore bougé. Le même titre republié (après une modification) est relu sur place,
    /// sans clignoter.
    /// </summary>
    private void ScheduleExtras()
    {
        ++_generation;
        _timer?.Stop();
        if (Detail is not { } detail)
        {
            ApplyExtras(null);
            return;
        }

        if (Avalonia.Application.Current is null || _loadedFor == detail.Id)
        {
            ApplyExtras(ReadExtras(detail.Id));
            return;
        }

        ApplyExtras(null);
        if (_timer is null)
        {
            _timer = new Avalonia.Threading.DispatcherTimer { Interval = Delay };
            _timer.Tick += async (_, _) =>
            {
                _timer.Stop();
                await LoadExtrasAsync();
            };
        }

        _timer.Start();
    }

    private async Task LoadExtrasAsync()
    {
        if (Detail is not { } detail)
        {
            return;
        }

        var generation = _generation;
        var extras = await Task.Run(() => ReadExtras(detail.Id));
        if (generation == _generation && Detail?.Id == extras.TitleId)
        {
            ApplyExtras(extras);
        }
    }

    /// <summary>Lectures seules, chacune sur sa propre connexion : sûr hors du fil d'interface.</summary>
    private Extras ReadExtras(long titleId)
    {
        var (cast, directors, crew) = CreditsOf(titleId);
        return new Extras(
            titleId,
            MediaTextOf(titleId),
            SimilarOf(titleId),
            _extras.Suggestions?.For(titleId) ?? [],
            _extras.Suggestions?.WasFetched(titleId) ?? false,
            cast,
            directors,
            crew,
            _media?.People?.Repository.Credits(titleId) ?? []);
    }

    private void ApplyExtras(Extras? extras)
    {
        _loadedFor = extras?.TitleId;
        TechnicalSummary = extras?.Technical;
        Similar = extras?.Similar ?? [];
        TmdbSuggestions = extras?.Suggestions ?? [];
        SuggestionsFetched = extras?.SuggestionsFetched ?? false;
        ShowCredits(extras?.Cast ?? [], extras?.Directors ?? [], extras?.Crew);

        // Photos manquantes téléchargées en arrière-plan ; la distribution est rechargée si le titre est toujours affiché.
        if (extras is not null && extras.Credits.Any(c => c.PhotoRelativePath is null && c.ProfilePath is not null))
        {
            _ = LoadCreditPhotosAsync(extras.TitleId, extras.Credits);
        }
    }

    private void ShowCredits(IReadOnlyList<CastMember> cast, IReadOnlyList<CastMember> directors, string? crew)
    {
        Cast.Clear();
        Directors.Clear();
        foreach (var actor in cast)
        {
            Cast.Add(actor);
        }

        foreach (var director in directors)
        {
            Directors.Add(director);
        }

        CrewText = string.IsNullOrEmpty(crew) ? null : crew;
        OnPropertyChanged(nameof(HasCast));
        OnPropertyChanged(nameof(HasDirectors));

        // Après le générique, jamais avant : « Voir aussi » doit nommer les personnes du film affiché.
        SelectionPeople = Detail is null
            ? []
            :
            [
                .. Directors.Select(d => new FacetItem(
                    Tr.F("Films de {0}", d.Name), new RuleSearch([new SearchRule("director", RuleOperator.Equals, d.Name)]))),
                .. Cast.Take(6).Select(a => new FacetItem(
                    Tr.F("Films avec {0}", a.Name), new RuleSearch([new SearchRule("actor", RuleOperator.Equals, a.Name)]))),
            ];
    }

    private async Task LoadCreditPhotosAsync(long titleId, IReadOnlyList<CastMember> credits)
    {
        try
        {
            if (_media?.People is { } people &&
                await people.EnsureCreditPhotosAsync(credits.Where(c => c.Role == "actor")) > 0 && Detail?.Id == titleId)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (Detail?.Id == titleId)
                    {
                        var (cast, directors, crew) = CreditsOf(titleId);
                        ShowCredits(cast, directors, crew);
                    }
                });
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            // Pas de photo : la fiche reste utilisable.
        }
    }
}
