using System.Collections.ObjectModel;
using System.Globalization;
using Cinedex.App.Services;
using Cinedex.Core;
using Cinedex.Core.Metadata;
using Cinedex.Core.People;
using Cinedex.Core.Titles;
using Cinedex.Data;
using Cinedex.Media;
using Cinedex.Metadata.Tmdb;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cinedex.Core.Localization;

namespace Cinedex.App.ViewModels;

public sealed record Choice<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Entrée de la barre latérale (type non générique, utilisable dans les modèles XAML).</summary>
public sealed record FilterItem(QuickFilter Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Fenêtre principale : liste filtrée et triée, panneau de détail, actions sur la sélection.</summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly TitleRepository _titles;
    private readonly AppPaths _paths;
    private readonly IDialogs _dialogs;
    private readonly MetadataService? _metadata;
    private readonly ImageStore? _images;
    private readonly Func<SettingsViewModel>? _settings;
    private readonly MediaContext? _media;
    private readonly CatalogsViewModel? _catalogs;
    private bool _refreshing;
    private bool _loaded;

    public MainWindowViewModel(
        TitleRepository titles,
        AppPaths paths,
        IDialogs dialogs,
        MetadataService? metadata = null,
        ImageStore? images = null,
        Func<SettingsViewModel>? settings = null,
        MediaContext? media = null,
        CatalogsViewModel? catalogs = null)
    {
        _titles = titles;
        _paths = paths;
        _dialogs = dialogs;
        _metadata = metadata;
        _images = images;
        _settings = settings;
        _media = media;
        _catalogs = catalogs;
        ApplyDisplaySettings();
        SelectedFilter = Filters[0];
        SelectedSort = Sorts[0];
        SelectedTitles.CollectionChanged += (_, _) =>
        {
            if (!_refreshing)
            {
                OnSelectionChanged();
            }
        };
    }

    public ObservableCollection<TitleSummary> Titles { get; } = [];

    public ObservableCollection<TitleSummary> SelectedTitles { get; } = [];

    /// <summary>Toutes les vues de la liste principale du bandeau ; celles masquées dans les réglages sont retirées de <see cref="Filters"/>.</summary>
    public static IReadOnlyList<FilterItem> AllMainFilters { get; } =
    [
        new(QuickFilter.All, "Tous les titres"),
        new(QuickFilter.Owned, "Possédés"),
        new(QuickFilter.Wishlist, "Liste de souhaits"),
        new(QuickFilter.Seen, "Vus"),
        new(QuickFilter.Unseen, "Pas encore vus"),
        new(QuickFilter.Favorites, "Favoris"),
        new(QuickFilter.Shortlist, "Liste courte"),
        new(QuickFilter.Movies, "Films seulement"),
        new(QuickFilter.Series, "Séries seulement"),
        new(QuickFilter.Loaned, "Prêtés"),
        new(QuickFilter.MediaFiles, "Fichiers vidéo"),
    ];

    /// <summary>Sous-menu « Définition » du bandeau gauche.</summary>
    public static IReadOnlyList<FilterItem> AllDefinitionFilters { get; } =
    [
        new(QuickFilter.StandardDefinition, "Définition standard"),
        new(QuickFilter.HighDefinition, "Haute définition"),
        new(QuickFilter.UltraHighDefinition, "Ultra HD (4K)"),
        new(QuickFilter.ThreeD, "3D"),
    ];

    /// <summary>Vues affichées dans le bandeau gauche (selon les réglages du catalogue).</summary>
    [ObservableProperty]
    public partial IReadOnlyList<FilterItem> Filters { get; private set; } = AllMainFilters;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDefinitionFilters))]
    public partial IReadOnlyList<FilterItem> DefinitionFilters { get; private set; } = AllDefinitionFilters;

    public bool HasDefinitionFilters => DefinitionFilters.Count > 0;

    private DisplaySettings _display = new();

    public bool ShowRuleSearch => _display.ShowRuleSearch;

    /// <summary>Infobulle au survol des films de la liste (réglage, désactivé par défaut).</summary>
    public bool ShowTooltips => _display.ShowTooltips;

    /// <summary>Titres dont la fiche TMDb attend un choix (liste « À vérifier »).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReviews), nameof(ReviewLabel))]
    public partial int ReviewCount { get; set; }

    public bool HasReviews => ReviewCount > 0;

    public string ReviewLabel => Tr.F("À vérifier ({0})", ReviewCount);

    /// <summary>Journal des actions (Réglages > Général) : une ligne par action faite dans l'application.</summary>
    private static void Journal(string message) => ActivityJournal.Current.Write(message);

    /// <summary>Titres désignés dans le journal : « Heat », ou « Heat, Ran et 3 autres ».</summary>
    private static string Names(List<TitleSummary> titles) =>
        titles.Count <= 3 ? string.Join(", ", titles.Select(t => t.Title)) : string.Join(", ", titles.Take(2).Select(t => t.Title)) + $" et {titles.Count - 2} autres";

    /// <summary>Aide (F1) dans la langue de l'interface : français, sinon anglais.</summary>
    [RelayCommand]
    private void OpenHelp() => _dialogs.ShowHelp(new HelpViewModel(HelpDocument.Load(Cinedex.Core.Localization.Tr.Current.Code)));

    [RelayCommand]
    private async Task OpenReviewAsync()
    {
        if (_metadata?.Reviews is not { } reviews)
        {
            return;
        }

        await _dialogs.ShowMatchReviewAsync(new MatchReviewViewModel(reviews, _metadata));
        Refresh(selectIds: SelectedIds);
    }

    public bool ShowPeopleLink => HasPeople && _display.ShowPeople;

    /// <summary>Économiseur d'écran (Réglages > Économiseur) : relu avec les autres réglages d'affichage.</summary>
    public ScreenSaverSettings ScreenSaver { get; private set; } = new();

    /// <summary>Diaporama des affiches du catalogue, ou null s'il n'y a aucune affiche à montrer.</summary>
    public ScreenSaverViewModel? CreateScreenSaver()
    {
        if (_images is null)
        {
            return null;
        }

        var slides = _titles.PosterSlides(_display.HideAdult)
            .Select(p => _images.ResolveInside(p.PosterRelativePath) is { } poster
                ? new ScreenSaverSlide(p.TitleId, p.Title, p.Year, poster, p.BackdropRelativePath is null ? null : _images.ResolveInside(p.BackdropRelativePath))
                : null)
            .OfType<ScreenSaverSlide>()
            .ToList();
        if (slides.Count == 0)
        {
            return null;
        }

        Journal($"Économiseur d'écran lancé ({slides.Count} affiches)");
        return new ScreenSaverViewModel(slides, TimeSpan.FromSeconds(ScreenSaver.IntervalSeconds), ScreenSaver.ShowTitles);
    }

    /// <summary>Lien « Collections » du bandeau gauche (Réglages > Général, activé par défaut).</summary>
    public bool ShowCollections => _metadata?.Collections is not null && _display.ManageCollections;

    [RelayCommand]
    private async Task OpenCollectionsAsync()
    {
        if (_metadata?.Collections is not { } collections)
        {
            return;
        }

        var browser = new CollectionsViewModel(collections);
        var chosen = await _dialogs.ShowCollectionsAsync(browser) && browser.ChosenTitleId is not null;
        if (chosen)
        {
            GoToTitle(browser.ChosenTitleId!.Value);
        }
        else if (browser.Changed)
        {
            Refresh(selectIds: SelectedIds);
        }
    }

    /// <summary>Relit les réglages d'affichage : vues visibles, liens, contenu pour adultes.</summary>
    private void ApplyDisplaySettings()
    {
        _display = _media is null ? new DisplaySettings() : DisplaySettings.Load(_media.Settings);
        ScreenSaver = _media is null ? new ScreenSaverSettings() : ScreenSaverSettings.Load(_media.Settings);
        Filters = AllMainFilters.Where(f => f.Value == QuickFilter.All || !_display.HiddenFilters.Contains(f.Value)).ToList();
        DefinitionFilters = AllDefinitionFilters.Where(f => !_display.HiddenFilters.Contains(f.Value)).ToList();
        OnPropertyChanged(nameof(ShowRuleSearch));
        OnPropertyChanged(nameof(ShowTooltips));
        OnPropertyChanged(nameof(ShowPeopleLink));
        OnPropertyChanged(nameof(ShowCollections));
        if (SelectedFilter is not null && !Filters.Contains(SelectedFilter) && !DefinitionFilters.Contains(SelectedFilter))
        {
            SelectedFilter = Filters[0];
        }
        else
        {
            OnPropertyChanged(nameof(SelectedMainFilter));
            OnPropertyChanged(nameof(SelectedDefinitionFilter));
        }
    }

    /// <summary>Sélection de la liste principale ; vide quand une vue du sous-menu est choisie (et inversement).</summary>
    public FilterItem? SelectedMainFilter
    {
        get => Filters.Contains(SelectedFilter) ? SelectedFilter : null;
        set
        {
            if (value is not null)
            {
                SelectedFilter = value;
            }
        }
    }

    public FilterItem? SelectedDefinitionFilter
    {
        get => DefinitionFilters.Contains(SelectedFilter) ? SelectedFilter : null;
        set
        {
            if (value is not null)
            {
                SelectedFilter = value;
            }
        }
    }

    [ObservableProperty]
    public partial bool IsDefinitionExpanded { get; set; }

    [RelayCommand]
    private void ToggleDefinitions() => IsDefinitionExpanded = !IsDefinitionExpanded;

    public IReadOnlyList<Choice<SortField>> Sorts { get; } =
    [
        new(SortField.Title, "Titre"),
        new(SortField.Year, "Année"),
        new(SortField.Number, "Numéro"),
        new(SortField.AddedAt, "Date d'ajout"),
        new(SortField.UserRating, "Note personnelle"),
        new(SortField.Runtime, "Durée"),
    ];

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial FilterItem SelectedFilter { get; set; }

    [ObservableProperty]
    public partial Choice<SortField> SelectedSort { get; set; }

    [ObservableProperty]
    public partial bool SortDescending { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    public partial string NewTitleText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetail))]
    [NotifyCanExecuteChangedFor(nameof(LendCommand), nameof(ReturnLoanCommand))]
    public partial TitleDetail? Detail { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LendCommand))]
    public partial string BorrowerText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? PosterPath { get; set; }

    [ObservableProperty]
    public partial string? BackdropPath { get; set; }

    /// <summary>Vue affiches (grille) ou liste détaillée.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsListView))]
    public partial bool IsGridView { get; set; }

    public bool IsListView => !IsGridView;

    private const string GridViewKey = "ui.gridView";

    // La vue choisie est mémorisée pour le catalogue.
    partial void OnIsGridViewChanged(bool value) => _media?.Settings.Set(GridViewKey, value ? "true" : "false");

    [RelayCommand]
    private void ShowGrid() => IsGridView = true;

    [RelayCommand]
    private void ShowList() => IsGridView = false;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand), nameof(OpenFolderCommand))]
    public partial IReadOnlyList<MediaFileEntry> DetailFiles { get; set; } = [];

    [ObservableProperty]
    public partial string? TechnicalSummary { get; set; }

    /// <summary>Distribution (acteurs, avec photo et personnage) du titre affiché.</summary>
    public ObservableCollection<CastMember> Cast { get; } = [];

    [ObservableProperty]
    public partial string? CrewText { get; set; }

    public bool HasCast => Cast.Count > 0;

    public bool HasPeople => _media?.People is not null;

    private void LoadCredits()
    {
        Cast.Clear();
        CrewText = null;
        if (Detail is not { } detail || _media?.People is not { } people)
        {
            OnPropertyChanged(nameof(HasCast));
            return;
        }

        var credits = people.Repository.Credits(detail.Id);
        foreach (var actor in credits.Where(c => c.Role == "actor"))
        {
            Cast.Add(actor with { PhotoPath = people.ResolvePhoto(actor.PhotoRelativePath) });
        }

        var crew = credits.Where(c => c.Role != "actor").GroupBy(c => c.RoleLabel)
            .Select(g => $"{g.Key} : {string.Join(", ", g.Select(c => c.Name).Distinct())}");
        CrewText = string.Join('\n', crew);
        OnPropertyChanged(nameof(HasCast));

        // Photos manquantes téléchargées en arrière-plan ; la distribution est rechargée si le titre est toujours affiché.
        if (credits.Any(c => c.PhotoRelativePath is null && c.ProfilePath is not null))
        {
            _ = LoadCreditPhotosAsync(detail.Id, credits);
        }
    }

    private async Task LoadCreditPhotosAsync(long titleId, IReadOnlyList<CastMember> credits)
    {
        try
        {
            if (await _media!.People!.EnsureCreditPhotosAsync(credits.Where(c => c.Role == "actor")) > 0 && Detail?.Id == titleId)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(LoadCredits);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            // Pas de photo : la fiche reste utilisable.
        }
    }

    /// <summary>Fiche d'une personne ; si l'un de ses titres y est choisi, il est sélectionné dans le catalogue.</summary>
    [RelayCommand]
    private async Task OpenPersonAsync(CastMember? member)
    {
        if (member is null || _media?.People is not { } people)
        {
            return;
        }

        var page = new PersonViewModel(people, member.PersonId);
        if (await _dialogs.ShowPersonAsync(page) && page.ChosenTitleId is { } titleId)
        {
            GoToTitle(titleId);
        }
    }

    [RelayCommand]
    private async Task OpenPeopleAsync()
    {
        if (_media?.People is not { } people)
        {
            return;
        }

        var browser = new PeopleBrowserViewModel(people);
        if (await _dialogs.ShowPeopleAsync(browser) && browser.ChosenTitleId is { } titleId)
        {
            GoToTitle(titleId);
        }
    }

    /// <summary>Sélectionne un titre, en levant recherche et filtres s'il n'est pas affiché.</summary>
    public void GoToTitle(long titleId)
    {
        if (Titles.All(t => t.Id != titleId))
        {
            _loaded = false;
            SearchText = string.Empty;
            SelectedFilter = Filters[0];
            Rules = null;
            _loaded = true;
        }

        Refresh(selectId: titleId);
    }

    public bool HasDetailFiles => DetailFiles.Count > 0;

    partial void OnDetailFilesChanged(IReadOnlyList<MediaFileEntry> value) => OnPropertyChanged(nameof(HasDetailFiles));

    /// <summary>Emplacement complet et infos MediaInfo de chaque fichier du titre, ou une indication s'il n'y en a pas.</summary>
    private string MediaTextOf(long titleId)
    {
        var files = _media?.Files.TechnicalDetails(titleId) ?? [];
        return files.Count == 0
            ? "Aucun fichier vidéo rattaché à ce titre (ajouté depuis TMDb ou à la main). Utilisez « Importer » pour l'associer à son fichier."
            : string.Join("\n\n", files.Select(f => f.Describe()));
    }

    public bool HasMedia => _media is not null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddFromTmdbCommand), nameof(LinkToTmdbCommand), nameof(UpdateAllFromTmdbCommand))]
    public partial bool IsBusy { get; set; }

    public bool HasMetadataSource => _metadata is not null;

    public bool HasDetail => Detail is not null;

    public bool IsSeries => Detail?.Kind == TitleKind.Series;

    public bool IsEmpty => _loaded && Titles.Count == 0;

    public string DataLocation => _paths.IsPortable ? Tr.F("Mode portable : {0}", _paths.DataRoot) : _paths.DataRoot;

    /// <summary>Apparence choisie (commune à tous les catalogues), appliquée et mémorisée dès qu'elle change.</summary>
    public AppTheme Theme
    {
        get => Appearance.Load(_paths.DataRoot);
        set
        {
            Appearance.Save(_paths.DataRoot, value);
            Appearance.Apply(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSystemTheme));
            OnPropertyChanged(nameof(IsLightTheme));
            OnPropertyChanged(nameof(IsDarkTheme));
        }
    }

    public bool IsSystemTheme => Theme == AppTheme.System;

    public bool IsLightTheme => Theme == AppTheme.Light;

    public bool IsDarkTheme => Theme == AppTheme.Dark;

    [RelayCommand]
    private void SetTheme(AppTheme theme) => Theme = theme;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCriteria))]
    public partial RuleSearch? Rules { get; set; }

    /// <summary>Une recherche multicritère est active (le bouton d'effacement apparaît).</summary>
    public bool HasCriteria => Rules is { IsEmpty: false };

    partial void OnRulesChanged(RuleSearch? value) => Refresh();

    private TitleQuery CurrentQuery => new(SearchText, SelectedFilter.Value, SelectedSort.Value, SortDescending, _display.HideAdult, Rules: Rules);

    private IReadOnlyList<long> SelectedIds => SelectedTitles.Select(t => t.Id).ToList();

    public void Load()
    {
        _loaded = true;
        IsGridView = _media?.Settings.Get(GridViewKey) == "true";
        Refresh();
    }

    private Avalonia.Threading.DispatcherTimer? _searchTimer;

    /// <summary>
    /// Recherche à la frappe : dans l'application, la liste n'est recalculée qu'après une courte pause de saisie
    /// (une requête par mot plutôt qu'une par lettre) ; hors interface (tests), immédiatement.
    /// </summary>
    partial void OnSearchTextChanged(string value)
    {
        if (Avalonia.Application.Current is null || string.IsNullOrEmpty(value))
        {
            _searchTimer?.Stop();
            Refresh();
            return;
        }

        if (_searchTimer is null)
        {
            _searchTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _searchTimer.Tick += (_, _) =>
            {
                _searchTimer.Stop();
                Refresh();
            };
        }

        _searchTimer.Stop();
        _searchTimer.Start();
    }

    partial void OnSelectedFilterChanged(FilterItem value)
    {
        OnPropertyChanged(nameof(SelectedMainFilter));
        OnPropertyChanged(nameof(SelectedDefinitionFilter));
        if (DefinitionFilters.Contains(value))
        {
            IsDefinitionExpanded = true;
        }

        Refresh();
    }

    partial void OnSelectedSortChanged(Choice<SortField> value) => Refresh();

    partial void OnSortDescendingChanged(bool value) => Refresh();

    private void OnSelectionChanged()
    {
        Detail = SelectedTitles.Count == 1 ? _titles.Get(SelectedTitles[0].Id) : null;
        PosterPath = Detail is null ? null : _images?.PathFor(Detail.Id, "poster");
        BackdropPath = Detail is null ? null : _images?.PathFor(Detail.Id, "backdrop");
        DetailFiles = Detail is null || _media is null ? [] : _media.Files.FilesOf(Detail.Id);
        TechnicalSummary = Detail is null ? null : MediaTextOf(Detail.Id);
        LoadCredits();
        LinkToTmdbCommand.NotifyCanExecuteChanged();
        OpenSeriesCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsSeries));
        BorrowerText = Detail?.CurrentLoan is null ? _titles.LastBorrower() ?? string.Empty : string.Empty;
        EditCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        ToggleSeenCommand.NotifyCanExecuteChanged();
        ToggleFavoriteCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Add()
    {
        var (title, year) = TitleText.SplitYear(NewTitleText);
        var id = _titles.Add(new NewTitle(TitleKind.Movie, title, year));
        Journal($"Titre ajouté à la main : {title}{(year is { } y ? $" ({y})" : string.Empty)}");
        NewTitleText = string.Empty;
        Refresh(selectId: id);
    }

    private bool CanAdd() => !string.IsNullOrWhiteSpace(NewTitleText);

    /// <summary>Un titre : fiche complète. Plusieurs : modification groupée.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task EditAsync()
    {
        if (SelectedTitles.Count > 1)
        {
            await BulkEditAsync();
            return;
        }

        var current = _titles.Get(SelectedTitles[0].Id);
        if (current is null)
        {
            return;
        }

        // La fiche reprend tout ce que montre le panneau de droite : affiche, notes, distribution, fichiers.
        var extras = new TitleEditorExtras(
            _images?.PathFor(current.Id, "poster"),
            Detail?.Id == current.Id ? Cast.ToList() : null,
            Detail?.Id == current.Id ? CrewText : null,
            MediaTextOf(current.Id),
            _media is null ? null : async () =>
            {
                var (_, tool) = await _media.Import.RefreshTechnicalAsync([current.Id], new Progress<string>(m => Status = m), CancellationToken.None);
                Status = tool == "aucun" ? "MediaInfo introuvable : indiquez son chemin dans les réglages." : Tr.F("Infos techniques relues avec {0}", tool);
                return MediaTextOf(current.Id);
            },
            _media is null ? null : async path =>
            {
                var part = await _media.Import.AddPartAsync(current.Id, path, videoExtensions: ScanSettings.Load(_media.Settings).ExtensionSet);
                Status = Tr.F("Partie {0} rattachée à « {1} »", part, current.Title);
                Journal($"Partie {part} rattachée à « {current.Title} » : {path}");
                return MediaTextOf(current.Id);
            },
            _media?.People,
            new LoanEditorViewModel(_titles, current.Id),
            _media?.Files);
        var editor = new TitleEditorViewModel(
            current, _titles.Genres(), _titles.RefItems("medium"), _titles.RefItems("case"), _titles.RefItems("version"), _titles.Collections(), extras);
        // La fenêtre d'édition valide elle-même (TryBuild) avant de se fermer sur « Enregistrer ».
        if (await _dialogs.EditTitleAsync(editor) && editor.TryBuild(out var updated))
        {
            _titles.Update(updated);
            Journal($"Fiche modifiée : {updated.Title}");
            try
            {
                editor.ApplyFileChanges();
            }
            catch (InvalidOperationException ex)
            {
                Status = ex.Message;
            }

            if (editor.SeenChanged)
            {
                _titles.SetSeen([updated.Id], editor.IsSeen, DateTimeOffset.Now);
                Journal($"Marqué {(editor.IsSeen ? "vu" : "non vu")} : {updated.Title}");
            }

            Refresh(selectId: updated.Id);
        }
        else if (editor.NavigateToTitleId is { } target)
        {
            // Un film choisi depuis la fiche d'un acteur.
            GoToTitle(target);
        }
        else
        {
            // Infos techniques relues, partie ajoutée ou prêt modifié depuis la fiche, même sans enregistrer.
            Refresh(selectId: current.Id);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task DeleteAsync()
    {
        var ids = SelectedIds;
        var question = ids.Count == 1
            ? Tr.F("Supprimer « {0} » du catalogue ?", SelectedTitles[0].Title)
            : Tr.F("Supprimer {0} titres du catalogue ?", ids.Count);
        if (await _dialogs.ConfirmAsync("Supprimer", question + "\n" + Tr.T("Les fichiers vidéo sur le disque ne sont pas touchés.")))
        {
            var names = Names(SelectedTitles.ToList());
            var count = _titles.Delete(ids);
            Journal($"{count} titre(s) supprimé(s) du catalogue : {names}");
            Refresh();
            Status = Tr.F("{0} titre(s) supprimé(s)", count);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void ToggleSeen()
    {
        var ids = SelectedIds;
        var markSeen = SelectedTitles.Any(t => !t.IsSeen);
        _titles.SetSeen(ids, markSeen, DateTimeOffset.Now);
        Journal($"Marqué {(markSeen ? "vu" : "non vu")} : {Names(SelectedTitles.ToList())}");
        Refresh(selectIds: ids);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void ToggleFavorite()
    {
        var ids = SelectedIds;
        var favorite = SelectedTitles.Any(t => !t.IsFavorite);
        _titles.SetFavorite(ids, favorite);
        Journal($"{(favorite ? "Ajouté aux" : "Retiré des")} favoris : {Names(SelectedTitles.ToList())}");
        Refresh(selectIds: ids);
    }

    [RelayCommand]
    private void PickRandom()
    {
        var pick = _titles.PickRandom(CurrentQuery, unseenOnly: true);
        if (pick is null)
        {
            Status = "Aucun titre non vu dans la sélection courante.";
            return;
        }

        Refresh(selectId: pick.Id);
        Status = Tr.F("Au hasard : {0}", pick.Title);
    }

    [RelayCommand(CanExecute = nameof(CanLend))]
    private void Lend()
    {
        try
        {
            _titles.Lend(Detail!.Id, BorrowerText, DateTimeOffset.Now);
            Journal($"Prêté à {BorrowerText.Trim()} : {Detail.Title}");
            Refresh(selectId: Detail.Id);
        }
        catch (InvalidOperationException ex)
        {
            Status = ex.Message;
        }
    }

    private bool CanLend() => Detail is { CurrentLoan: null } && !string.IsNullOrWhiteSpace(BorrowerText);

    [RelayCommand(CanExecute = nameof(CanReturn))]
    private void ReturnLoan()
    {
        _titles.Return(Detail!.Id, DateTimeOffset.Now);
        Journal($"Rendu : {Detail.Title}");
        Refresh(selectId: Detail.Id);
    }

    private bool CanReturn() => Detail is { CurrentLoan: not null };

    [RelayCommand]
    private async Task ShowLoanHistoryAsync()
    {
        if (Detail is null)
        {
            return;
        }

        var history = _titles.LoanHistory(Detail.Id);
        var text = history.Count == 0
            ? "Aucun prêt enregistré pour ce titre."
            : string.Join('\n', history.Select(LoanLine.Describe));
        await _dialogs.InformAsync(Tr.F("Historique des prêts : {0}", Detail.Title), text);
    }

    /// <summary>Recherche sur TMDb (texte saisi ou boîte de recherche) et importe le titre choisi.</summary>
    [RelayCommand(CanExecute = nameof(CanUseMetadata))]
    private async Task AddFromTmdbAsync()
    {
        var search = new MetadataSearchViewModel(_metadata!, NewTitleText);
        if (await _dialogs.SearchMetadataAsync(search) is not { } choice)
        {
            return;
        }

        await RunMetadataAsync(async () =>
        {
            var (id, created) = await _metadata!.ImportAsync(choice, ImportOptions.Default);
            NewTitleText = string.Empty;
            Refresh(selectId: id);
            Status = created ? Tr.F("« {0} » ajouté depuis TMDb", choice.Title) : Tr.F("« {0} » est déjà dans le catalogue", choice.Title);
            Journal(created ? $"Ajouté depuis TMDb : {choice.Title} (TMDb {choice.SourceId})" : $"Déjà au catalogue : {choice.Title}");
        });
    }

    /// <summary>
    /// Associe le titre sélectionné à une fiche TMDb (recherche par son titre et son année) ou, s'il l'est déjà,
    /// rafraîchit sa fiche. Un titre verrouillé n'est pas modifié.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanLink))]
    private async Task LinkToTmdbAsync()
    {
        var detail = Detail!;
        var kind = detail.Kind;
        long sourceId;
        if (detail.TmdbId is { } known)
        {
            sourceId = known;
        }
        else
        {
            var query = detail.Year is { } y ? $"{detail.Title} ({y})" : detail.Title;
            var search = new MetadataSearchViewModel(_metadata!, query);
            if (await _dialogs.SearchMetadataAsync(search) is not { } choice)
            {
                return;
            }

            (kind, sourceId) = (choice.Kind, choice.SourceId);
        }

        await RunMetadataAsync(async () =>
        {
            var applied = await _metadata!.UpdateAsync(detail.Id, kind, sourceId, ImportOptions.Default);
            Refresh(selectId: detail.Id);
            Status = applied ? "Fiche mise à jour depuis TMDb" : "Titre verrouillé : non modifié";
            Journal(applied ? $"Fiche mise à jour depuis TMDb : {detail.Title} (TMDb {sourceId})" : $"Titre verrouillé, non modifié : {detail.Title}");
        });
    }

    private bool CanLink() => CanUseMetadata() && Detail is not null;

    /// <summary>
    /// « Mettre à jour via TMDb » : la sélection, ou sinon tous les titres affichés. Met à jour les fiches
    /// existantes (identification par titre et année pour les titres scannés) ; ne crée jamais d'entrée.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanUseMetadata))]
    private async Task UpdateAllFromTmdbAsync()
    {
        var ids = SelectedTitles.Count > 0 ? SelectedIds : Titles.Select(t => t.Id).ToList();
        var scope = SelectedTitles.Count > 0 ? Tr.F("les {0} titre(s) sélectionné(s)", ids.Count) : Tr.F("les {0} titre(s) affiché(s)", ids.Count);
        if (ids.Count == 0 || !await _dialogs.ConfirmAsync(
                "Mettre à jour via TMDb",
                Tr.F("Mettre à jour {0} depuis TMDb ?\nLes fiches existantes sont complétées sur place, aucune entrée n'est créée. Les titres verrouillés sont ignorés.", scope)))
        {
            return;
        }

        await RunMetadataAsync(async () =>
        {
            var targets = ids.Select(_titles.Get).OfType<TitleDetail>().ToList();
            var progress = new Progress<string>(message => Status = message);
            var result = await _metadata!.UpdateTitlesAsync(targets, ImportOptions.Default, progress, CancellationToken.None);
            Refresh(selectIds: ids);
            Status = Tr.F("{0} mis à jour, {1} à vérifier, {2} non trouvé(s), {3} doublon(s) évité(s), {4} ignoré(s), {5} échec(s)", result.Updated, result.ToReview, result.NotFound, result.Duplicates, result.Skipped, result.Failed);
            Journal($"Mise à jour via TMDb de {targets.Count} titre(s) : {result.Updated} mis à jour, {result.ToReview} à vérifier, {result.NotFound} non trouvé(s), {result.Duplicates} doublon(s) évité(s), {result.Skipped} ignoré(s), {result.Failed} échec(s)");
            if (result.ToReview > 0)
            {
                // Les titres sans ambiguïté sont à jour ; il ne reste que les choix à faire.
                await OpenReviewAsync();
            }
            else if (result.Messages is { Count: > 0 } messages)
            {
                await _dialogs.InformAsync("Mise à jour via TMDb", Status + "\n\n" + string.Join('\n', messages.Take(30)));
            }
        });
    }

    private bool CanUseMetadata() => _metadata is not null && !IsBusy;

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        if (_settings is not null)
        {
            var settings = _settings();
            var maintenance = settings.Maintenance;
            if (maintenance is not null)
            {
                // Renumérotation « selon le tri actuel » : tout le catalogue, dans l'ordre de tri de la liste.
                var sort = SelectedSort;
                var descending = SortDescending;
                maintenance.CurrentOrder = (
                    Tr.T(sort.Label) + (descending ? " ↓" : " ↑"),
                    () => _titles.Query(new TitleQuery(null, QuickFilter.All, sort.Value, descending)).Select(t => t.Id).ToList());
            }

            var saved = await _dialogs.EditSettingsAsync(settings);
            if (maintenance?.PendingRename is { } newName && _catalogs is not null)
            {
                // Le catalogue est fermé puis rouvert sous son nouveau nom : la fenêtre reçoit un nouveau contexte.
                _catalogs.RenameCurrent(newName);
                return;
            }

            if (saved)
            {
                Journal("Réglages enregistrés");
                ApplyDisplaySettings();
            }

            if (saved || maintenance?.Changed == true)
            {
                Refresh(selectIds: SelectedIds);
            }

            if (maintenance?.PendingTitleId is { } titleId)
            {
                GoToTitle(titleId);
            }
        }
    }

    private async Task RunMetadataAsync(Func<Task> action)
    {
        IsBusy = true;
        Status = "Connexion à TMDb...";
        _media?.Gate?.Reset();
        try
        {
            await action();
        }
        catch (ApiLimitAbortedException ex)
        {
            Status = ex.Message;
            Refresh(selectIds: SelectedIds);
        }
        catch (TmdbAuthenticationException ex)
        {
            Status = ex.Message;
            await OpenSettingsAsync();
        }
        catch (Exception ex) when (ex is TmdbUnavailableException or KeyNotFoundException or DuplicateTitleException or HttpRequestException or TaskCanceledException)
        {
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ImportFolderAsync()
    {
        if (_media is null)
        {
            return;
        }

        var import = new ImportFolderViewModel(_media.Import, ScanSettings.Load(_media.Settings));
        if (await _dialogs.ImportFolderAsync(import))
        {
            if (import.LastResult is ({ } files, { } r))
            {
                Journal($"Import depuis le disque de {files} fichier(s) : {r.Added} ajouté(s) avec TMDb, {r.WithoutMetadata} sans fiche TMDb, {r.Linked} déjà présent(s), {r.ToReview} à vérifier, {r.Failed} échec(s)");
            }
            Refresh(selectIds: SelectedIds);
        }

        // Tout ce qui était sans ambiguïté est fait : on enchaîne sur les choix qui restent.
        if (import.ToReview > 0)
        {
            await OpenReviewAsync();
        }
    }

    /// <summary>Fichier absent : le plus souvent un disque USB débranché, nommé par son MediaID.</summary>
    private string MissingMediaMessage() =>
        DetailFiles.Select(f => f.VolumeLabel).FirstOrDefault(l => l is not null) is { } label
            ? Tr.F("Fichier introuvable : branchez le disque « {0} ».", label)
            : "Fichier introuvable : le disque est peut-être débranché.";

    /// <summary>Lit le premier fichier présent ; marque le titre comme vu si le réglage le demande (par défaut).</summary>
    [RelayCommand(CanExecute = nameof(HasFiles))]
    private void Play()
    {
        var file = DetailFiles.FirstOrDefault(f => File.Exists(f.FullPath));
        if (file is null)
        {
            Status = MissingMediaMessage();
            return;
        }

        try
        {
            Player.Play(file.FullPath, _media!.Settings.Get(MediaContext.PlayerKey), _media.Settings.Get(MediaContext.PlayerArgsKey));
            Journal($"Lecture : {file.FullPath}");
            if (_media.Settings.Get(MediaContext.MarkSeenOnPlayKey) != "false")
            {
                _titles.SetSeen([file.TitleId], true, DateTimeOffset.Now);
                Refresh(selectId: file.TitleId);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Status = ex.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(HasFiles))]
    private void OpenFolder()
    {
        var file = DetailFiles.FirstOrDefault(f => File.Exists(f.FullPath));
        if (file is null)
        {
            Status = MissingMediaMessage();
            return;
        }

        try
        {
            Player.RevealInFolder(file.FullPath);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Status = ex.Message;
        }
    }

    private bool HasFiles() => DetailFiles.Count > 0;

    /// <summary>Saisons et épisodes de la série sélectionnée.</summary>
    [RelayCommand(CanExecute = nameof(IsSeriesSelected))]
    private async Task OpenSeriesAsync()
    {
        if (Detail is not { } detail || _media?.Series is not { } series)
        {
            return;
        }

        var vm = new SeriesViewModel(detail.Id, detail.Title, detail.TmdbId, series, _metadata, _media.Files, _media.Settings);
        if (await _dialogs.ShowSeriesAsync(vm))
        {
            Refresh(selectId: detail.Id);
        }
    }

    public bool IsSeriesSelected() => Detail?.Kind == TitleKind.Series && _media?.Series is not null;

    /// <summary>Actualise les notes IMDb de tout le catalogue depuis le fichier officiel (sans consommer le quota OMDb).</summary>
    [RelayCommand]
    private async Task RefreshImdbRatingsAsync()
    {
        if (_media?.Ratings is not { } ratings)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var count = await ratings.RefreshAllImdbAsync(new Progress<string>(m => Status = m));
            Refresh(selectIds: SelectedIds);
            Status = Tr.F("Notes IMDb actualisées pour {0} titre(s)", count);
            Journal($"Notes IMDb actualisées pour {count} titre(s)");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException)
        {
            Status = Tr.F("Fichier IMDb indisponible : {0}", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Crée ou complète le .nfo des fichiers de la sélection, ou de tous les titres affichés.</summary>
    [RelayCommand]
    private void WriteNfo()
    {
        if (_media?.Nfo is not { } nfo)
        {
            return;
        }

        var ids = SelectedTitles.Count > 0 ? SelectedIds : Titles.Select(t => t.Id).ToList();
        var written = ids.Sum(id => nfo.WriteForTitle(id));
        Status = Tr.F("{0} fichier(s) .nfo créé(s) ou complété(s)", written);
        Journal($"{written} fichier(s) .nfo créé(s) ou complété(s)");
    }

    /// <summary>Relit les infos techniques (MediaInfo) des fichiers de la sélection, ou de tous les titres affichés.</summary>
    [RelayCommand]
    private async Task RefreshTechnicalAsync()
    {
        if (_media is null)
        {
            return;
        }

        var ids = SelectedTitles.Count > 0 ? SelectedIds : Titles.Select(t => t.Id).ToList();
        IsBusy = true;
        try
        {
            var (read, tool) = await _media.Import.RefreshTechnicalAsync(ids, new Progress<string>(m => Status = m), CancellationToken.None);
            Refresh(selectIds: SelectedIds);
            Status = tool == "aucun"
                ? "MediaInfo introuvable : indiquez son chemin dans les réglages."
                : Tr.F("{0} fichier(s) analysé(s) avec {1}", read, tool);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CheckFilesAsync()
    {
        if (_media is null)
        {
            return;
        }

        IsBusy = true;
        Status = "Vérification des fichiers...";
        try
        {
            var (missing, relinked) = await _media.Import.CheckFilesAsync();
            Status = Tr.F("{0} fichier(s) introuvable(s) (disque non branché ?), {1} retrouvé(s) après déplacement", missing, relinked);
            Journal($"Vérification des fichiers : {missing} introuvable(s), {relinked} retrouvé(s) après déplacement");
            Refresh(selectIds: SelectedIds);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task BulkEditAsync()
    {
        var ids = SelectedIds;
        var edit = new BulkEditViewModel(ids.Count, _titles.RefItems("medium"), _titles.Collections());
        if (await _dialogs.BulkEditAsync(edit))
        {
            _titles.ApplyBulk(ids.ToList(), edit.Build());
            Refresh(selectIds: ids);
            Status = Tr.F("{0} titres modifiés", ids.Count);
            Journal($"Modification groupée de {ids.Count} titre(s)");
        }
    }

    [RelayCommand]
    private async Task AdvancedSearchAsync()
    {
        // Le décompte en direct suit le texte recherché et le filtre rapide en cours.
        var search = new RuleSearchViewModel(Rules, _titles.Genres(), rules => _titles.Query(CurrentQuery with { Rules = rules }).Count);
        if (await _dialogs.RuleSearchAsync(search))
        {
            Rules = search.Build();
        }
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
        SelectedFilter = Filters[0];
        Rules = null;
    }

    public string CatalogName => _catalogs?.Current ?? AppPaths.DefaultCatalogName;

    public string CatalogLabel => Tr.F("Catalogue : {0}", _catalogs?.Current ?? AppPaths.DefaultCatalogName);

    [RelayCommand]
    private async Task ManageCatalogsAsync()
    {
        if (_catalogs is not null)
        {
            await _dialogs.ManageCatalogsAsync(_catalogs);
        }
    }

    [RelayCommand]
    private void Reload() => Refresh(selectIds: SelectedIds);

    private bool HasSelection() => SelectedTitles.Count > 0;

    private bool HasSingleSelection() => SelectedTitles.Count == 1;

    private void Refresh(long? selectId = null, IReadOnlyList<long>? selectIds = null)
    {
        // Les propriétés initialisées dans le constructeur déclenchent Refresh avant que tout soit prêt.
        if (!_loaded)
        {
            return;
        }

        ReviewCount = _metadata?.Reviews?.Count() ?? 0;

        var keep = selectIds ?? (selectId is { } one ? [one] : []);
        _refreshing = true;
        Titles.Clear();
        foreach (var title in _titles.Query(CurrentQuery))
        {
            Titles.Add(title.PosterRelativePath is { } poster && _images is not null
                ? title with { PosterPath = _images.ResolveInside(poster) }
                : title);
        }

        SelectedTitles.Clear();
        foreach (var title in Titles.Where(t => keep.Contains(t.Id)))
        {
            SelectedTitles.Add(title);
        }

        _refreshing = false;
        OnSelectionChanged();
        Status = SelectedFilter.Value == QuickFilter.All && string.IsNullOrWhiteSpace(SearchText)
            ? Tr.F("{0} titre(s) dans le catalogue", Titles.Count)
            : Tr.F("{0} titre(s) trouvé(s)", Titles.Count);
        OnPropertyChanged(nameof(IsEmpty));
    }

    internal static (string Title, int? Year) ParseTitleAndYear(string input) => TitleText.SplitYear(input);
}
