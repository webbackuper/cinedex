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
    private readonly CatalogExtras _extras;
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
        CatalogsViewModel? catalogs = null,
        CatalogExtras? extras = null)
    {
        _extras = extras ?? new CatalogExtras();
        _titles = titles;
        _paths = paths;
        _dialogs = dialogs;
        _metadata = metadata;
        _images = images;
        _settings = settings;
        _media = media;
        _catalogs = catalogs;
        Duplicates = CreateDuplicates();
        SideBar = new SideBarViewModel(titles, _extras.Lists);
        SideBar.Requested += (_, request) => OnSideBarRequested(request);
        SideBar.GalleryRequested += (_, key) =>
        {
            if (key == SideBarViewModel.GenresKey)
            {
                ShowGenreGallery();
            }
        };
        SideBar.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SideBarViewModel.SelectedListName))
            {
                MoveUpCommand.NotifyCanExecuteChanged();
                MoveDownCommand.NotifyCanExecuteChanged();
            }
            else if (e.PropertyName == nameof(SideBarViewModel.SelectedReferenceList))
            {
                AddMissingToWishlistCommand.NotifyCanExecuteChanged();
            }
        };
        Record = new RecordViewModel(titles, images, media, _extras, Prepare);
        Record.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(RecordViewModel.Detail):
                    LendCommand.NotifyCanExecuteChanged();
                    ReturnLoanCommand.NotifyCanExecuteChanged();
                    PlayTrailerCommand.NotifyCanExecuteChanged();
                    break;
                case nameof(RecordViewModel.BorrowerText):
                    LendCommand.NotifyCanExecuteChanged();
                    break;
                case nameof(RecordViewModel.DetailFiles):
                    PlayCommand.NotifyCanExecuteChanged();
                    OpenFolderCommand.NotifyCanExecuteChanged();
                    break;
                case nameof(RecordViewModel.SuggestionsFetched) or nameof(RecordViewModel.TmdbSuggestions):
                    FetchSuggestionsCommand.NotifyCanExecuteChanged();
                    break;
            }
        };
        ApplyDisplaySettings();
        SelectedFilter = Filters[0];
        SelectedSort = Sorts[0];
        SelectedTitles.CollectionChanged += (_, e) =>
        {
            TrackSelection(e);
            if (_refreshing)
            {
                // Un lot en cours (relecture, sélection par plage) : il publie lui-même, une fois, à la fin.
                return;
            }

            if (Avalonia.Application.Current is null)
            {
                // Sans application (tests du modèle seul), rien ne viendrait exécuter une tâche différée.
                PublishSelection();
                return;
            }

            // La liste ajoute les éléments un par un : Ctrl+A sur 12 000 titres lève 12 000 événements. Un
            // seul passage suffit, une fois la rafale terminée, avant le prochain affichage.
            if (!_selectionPending)
            {
                _selectionPending = true;
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    _selectionPending = false;
                    PublishSelection();
                });
            }
        };
    }

    private readonly HashSet<long> _selectedIds = [];

    private bool _selectionPending;

    /// <summary>
    /// Tient l'ensemble des identifiants à jour d'après l'événement lui-même : une insertion ou un retrait
    /// par élément, au lieu de le reconstruire en entier à chaque fois (ce qui rendait la sélection de n
    /// titres quadratique).
    /// </summary>
    private void TrackSelection(System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case System.Collections.Specialized.NotifyCollectionChangedAction.Add when e.NewItems is not null:
                foreach (TitleSummary added in e.NewItems)
                {
                    _selectedIds.Add(added.Id);
                }

                break;
            case System.Collections.Specialized.NotifyCollectionChangedAction.Remove when e.OldItems is not null:
                foreach (TitleSummary removed in e.OldItems)
                {
                    _selectedIds.Remove(removed.Id);
                }

                break;
            default:
                _selectedIds.Clear();
                _selectedIds.UnionWith(SelectedTitles.Select(t => t.Id));
                break;
        }
    }

    /// <summary>Publie la sélection telle qu'elle est : l'ensemble pour la grille, puis la fiche et les commandes.</summary>
    private void PublishSelection()
    {
        SelectedIdSet = new HashSet<long>(_selectedIds);
        OnSelectionChanged();
    }

    /// <summary>Le titre est-il sélectionné ? Lu sur l'état courant, même si la publication est encore en attente.</summary>
    public bool IsSelected(long titleId) => _selectedIds.Contains(titleId);

    /// <summary>Identifiants sélectionnés : la grille d'affiches (virtualisée) s'en sert pour marquer la sélection.</summary>
    [ObservableProperty]
    public partial IReadOnlySet<long> SelectedIdSet { get; private set; } = new HashSet<long>();

    private int _gridAnchor = -1;

    /// <summary>
    /// Clic dans la grille d'affiches : sélection simple, Ctrl pour ajouter ou retirer un titre, Maj pour une plage
    /// depuis le dernier titre cliqué.
    /// </summary>
    public void SelectInGrid(int index, bool toggle, bool extend)
    {
        if (index < 0 || index >= Titles.Count)
        {
            return;
        }

        var title = Titles[index];
        if (extend && _gridAnchor >= 0 && _gridAnchor < Titles.Count)
        {
            var (from, to) = _gridAnchor <= index ? (_gridAnchor, index) : (index, _gridAnchor);
            SetSelection(Titles.Skip(from).Take(to - from + 1));
            return;
        }

        if (toggle)
        {
            if (!SelectedTitles.Remove(title))
            {
                SelectedTitles.Add(title);
            }
        }
        else
        {
            SetSelection([title]);
        }

        _gridAnchor = index;
    }

    /// <summary>Déplacement au clavier dans la grille ; renvoie le rang du titre choisi.</summary>
    public int MoveInGrid(int delta, bool extend)
    {
        if (Titles.Count == 0)
        {
            return -1;
        }

        var current = SelectedTitles.Count > 0 ? Titles.IndexOf(SelectedTitles[^1]) : -1;
        var target = current < 0 ? 0 : Math.Clamp(current + delta, 0, Titles.Count - 1);
        if (extend)
        {
            var anchor = _gridAnchor;
            SelectInGrid(target, toggle: false, extend: true);
            _gridAnchor = anchor;
        }
        else
        {
            SelectInGrid(target, toggle: false, extend: false);
        }

        return target;
    }

    private void SetSelection(IEnumerable<TitleSummary> titles)
    {
        _refreshing = true;
        SelectedTitles.Clear();
        foreach (var title in titles)
        {
            SelectedTitles.Add(title);
        }

        _refreshing = false;
        PublishSelection();
    }

    /// <summary>Titres affichés ; remplacés d'un bloc à chaque relecture (une seule notification pour 10 000 lignes).</summary>
    public TitleCollection Titles { get; } = [];

    public ObservableCollection<TitleSummary> SelectedTitles { get; } = [];

    /// <summary>Toutes les vues de la liste principale du bandeau ; celles masquées dans les réglages sont retirées de <see cref="Filters"/>.</summary>
    public static IReadOnlyList<FilterItem> AllMainFilters { get; } =
    [
        new(QuickFilter.All, "Tous les titres"),
        new(QuickFilter.Seen, "Vus"),
        new(QuickFilter.Unseen, "Pas encore vus"),
        new(QuickFilter.Favorites, "Favoris"),
        new(QuickFilter.Shortlist, "Liste courte"),
        new(QuickFilter.Movies, "Films seulement"),
        new(QuickFilter.Series, "Séries seulement"),
        new(QuickFilter.Loaned, "Prêtés"),
        new(QuickFilter.MediaFiles, "Fichiers vidéo"),
        new(QuickFilter.NoMetadata, "Films sans infos"),
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

    /// <summary>Mises à jour automatiques (version installée) ; absent en test.</summary>
    public UpdateService? Updates { get; set; }

    /// <summary>Version plus récente trouvée au démarrage : un bouton du bandeau gauche propose de l'installer.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate), nameof(UpdateLabel))]
    public partial string? UpdateVersion { get; private set; }

    public bool HasUpdate => UpdateVersion is not null;

    public string UpdateLabel => Tr.F("Mise à jour {0} disponible", UpdateVersion ?? string.Empty);

    /// <summary>Recherche discrète d'une mise à jour au démarrage (si la préférence est activée).</summary>
    public async Task CheckForUpdatesAtStartupAsync()
    {
        if (Updates is not { IsInstalled: true } updates || !UpdatePreferences.Load(_paths.DataRoot).CheckAtStartup)
        {
            return;
        }

        try
        {
            UpdateVersion = await updates.CheckAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException or TaskCanceledException)
        {
            // Hors ligne ou GitHub injoignable : on réessaiera au prochain démarrage.
            Journal($"Recherche de mise à jour impossible : {ex.Message}");
        }
    }

    /// <summary>Télécharge la mise à jour trouvée, puis redémarre Cinedex dessus (après confirmation).</summary>
    [RelayCommand]
    private async Task InstallUpdateAsync()
    {
        if (Updates is not { } updates || UpdateVersion is not { } version ||
            !await _dialogs.ConfirmAsync(Tr.T("Mise à jour"), Tr.F("Installer Cinedex {0} maintenant ? Cinedex se ferme et redémarre sur la nouvelle version.", version)))
        {
            return;
        }

        try
        {
            await updates.DownloadAsync(p => Avalonia.Threading.Dispatcher.UIThread.Post(() => Status = Tr.F("Téléchargement de la mise à jour : {0} %", p)));
            Journal($"Mise à jour vers {version}");
            updates.ApplyAndRestart();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException or TaskCanceledException)
        {
            Status = Tr.F("Mise à jour impossible : {0}", ex.Message);
        }
    }

    private bool _backupRunning;

    /// <summary>
    /// Sauvegarde planifiée (Réglages > Sauvegardes) : faite en arrière-plan si elle est due. Appelée à l'ouverture
    /// de la fenêtre, puis régulièrement.
    /// </summary>
    public async Task RunScheduledBackupAsync(bool atStartup)
    {
        if (_media?.Backup is not { } backup || _backupRunning)
        {
            return;
        }

        _backupRunning = true;
        try
        {
            if (await Task.Run(() => backup.RunIfDue(atStartup)) is { } path)
            {
                Journal($"Sauvegarde automatique : {path}");
                Status = Tr.F("Sauvegarde automatique faite : {0}", path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            Journal($"Sauvegarde automatique impossible : {ex.Message}");
            Status = Tr.F("Sauvegarde automatique impossible : {0}", ex.Message);
        }
        finally
        {
            _backupRunning = false;
        }
    }

    /// <summary>Colonnes de la vue liste (Réglages > Liste des films).</summary>
    [ObservableProperty]
    public partial ListColumns Columns { get; private set; } = new();

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
        if (Trail is { Kind: GalleryKind.Collections, Id: { } inside } &&
            browser.Collections.FirstOrDefault(c => c.Id == inside) is { } current)
        {
            // Ouverte depuis une collection : c'est elle que la fenêtre montre.
            browser.Selected = current;
        }

        var chosen = await _dialogs.ShowCollectionsAsync(browser) && browser.ChosenTitleId is not null;
        if (chosen)
        {
            GoToTitle(browser.ChosenTitleId!.Value);
        }
        else if (browser.Changed)
        {
            if (Gallery is { Kind: GalleryKind.Collections })
            {
                // Une collection renommée, supprimée ou complétée : la galerie se relit.
                ShowCollectionGallery();
            }

            Refresh(selectIds: SelectedIds);
        }
    }

    /// <summary>Relit les réglages d'affichage : vues visibles, liens, contenu pour adultes.</summary>
    private void ApplyDisplaySettings()
    {
        _display = _media is null ? new DisplaySettings() : DisplaySettings.Load(_media.Settings);
        ScreenSaver = _media is null ? new ScreenSaverSettings() : ScreenSaverSettings.Load(_media.Settings);
        Columns = _media is null ? new ListColumns() : ListColumns.Load(_media.Settings);
        Filters = AllMainFilters.Where(f => f.Value == QuickFilter.All || !_display.HiddenFilters.Contains(f.Value)).ToList();
        DefinitionFilters = AllDefinitionFilters.Where(f => !_display.HiddenFilters.Contains(f.Value)).ToList();
        OnPropertyChanged(nameof(ShowRuleSearch));
        OnPropertyChanged(nameof(ShowTooltips));
        OnPropertyChanged(nameof(ShowPeopleLink));
        OnPropertyChanged(nameof(ShowDirectorsLink));
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
                PeopleBrowser = null;
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
                PeopleBrowser = null;
                SelectedFilter = value;
            }
        }
    }

    [ObservableProperty]
    public partial bool IsDefinitionExpanded { get; set; }

    [RelayCommand]
    private void ToggleDefinitions() => IsDefinitionExpanded = !IsDefinitionExpanded;

    private bool CanMove() => SideBar.SelectedListName is not null && SelectedTitles.Count > 0 && SelectedSort.Value == SortField.ListOrder;

    [RelayCommand(CanExecute = nameof(CanMove))]
    private void MoveUp() => Move(-1);

    [RelayCommand(CanExecute = nameof(CanMove))]
    private void MoveDown() => Move(1);

    private void Move(int delta)
    {
        if (SideBar.SelectedListName is not { } list)
        {
            return;
        }

        var ids = SelectedIds;
        _titles.MoveInGroup(list, ids, delta);
        Refresh(selectIds: ids);
    }

    /// <summary>
    /// Films en double du bandeau gauche : leur propre modèle, que la fenêtre ne fait qu'héberger. Elle n'en
    /// retient que le groupe choisi, pour n'afficher que ses fiches, et le moment où une fusion a eu lieu,
    /// pour relire sa liste. Nul quand le catalogue n'offre pas la maintenance.
    /// </summary>
    public DuplicatesViewModel? Duplicates { get; }

    public bool HasDuplicates => Duplicates is not null;

    private IReadOnlyList<long>? _duplicateIds;

    private DuplicatesViewModel? CreateDuplicates()
    {
        if (Merger is not { } merger)
        {
            return null;
        }

        var duplicates = new DuplicatesViewModel(merger, _dialogs.ConfirmAsync);
        duplicates.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DuplicatesViewModel.Selected))
            {
                OnDuplicateGroupChosen(duplicates.Selected);
            }
            else if (e.PropertyName == nameof(DuplicatesViewModel.Message) && duplicates.Message is { } message)
            {
                Status = message;
            }
        };
        duplicates.Merged += (_, _) =>
        {
            ReloadSideLists();
            Refresh();

            // La relecture annonce le nombre de titres ; ce qu'on veut lire ici, c'est ce que la fusion a fait.
            if (duplicates.Message is { } done)
            {
                Status = done;
            }
        };
        return duplicates;
    }

    /// <summary>La fusion de fiches, sauvegarde comprise, pour le bandeau comme pour le clic droit.</summary>
    private DuplicateMerger? Merger => _merger ??= _extras.Maintenance is { } maintenance
        ? new DuplicateMerger(maintenance, _paths.BackupsDirectory)
        : null;

    private DuplicateMerger? _merger;

    private void OnDuplicateGroupChosen(DuplicateGroupItem? group)
    {
        // Les identifiants d'abord : ce qui suit peut relire la liste, et elle doit déjà être la bonne.
        _duplicateIds = group?.TitleIds;
        if (group is not null)
        {
            Home = null;
            PeopleBrowser = null;
            SideBar.ReleaseAll();
            Rules = null;
        }

        RefreshView(group?.TitleIds);
    }

    /// <summary>Une autre vue est choisie : le groupe de doublons affiché est relâché.</summary>
    private void ReleaseDuplicateGroup()
    {
        if (Duplicates is { Selected: not null } duplicates)
        {
            duplicates.Selected = null;
        }
    }

    private bool CanAddMissing() => _extras.Lists is not null && _metadata is not null && !IsBusy && SideBar.SelectedReferenceList is { Missing: > 0 };

    /// <summary>
    /// Ajoute à la liste de souhaits les films de la liste choisie qui manquent au catalogue, étiquetés à son
    /// nom. Un appel TMDb par film : c'est long, et c'est pourquoi on le demande avant de le faire.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAddMissing))]
    private async Task AddMissingToWishlistAsync()
    {
        if (_extras.Lists is not { } service || SideBar.SelectedReferenceList is not { } list)
        {
            return;
        }

        if (!await _dialogs.ConfirmAsync(
                "Compléter la liste de souhaits",
                Tr.F("Ajouter à votre liste de souhaits les {0} film(s) de « {1} » que vous n'avez pas ?\nChaque film demande sa fiche à TMDb : comptez une poignée de minutes. Ils seront marqués non possédés et étiquetés « {1} ».", list.Missing, list.Name)))
        {
            return;
        }

        await RunMetadataAsync(async () =>
        {
            var progress = TrackedProgress(list.Missing);
            var (added, notFound) = await service.AddMissingToWishlistAsync(list, progress, CancellationToken.None);
            SideBar.Reload();
            Refresh();
            Status = Tr.F("{0} film(s) ajouté(s) à la liste de souhaits, {1} introuvable(s) sur TMDb", added, notFound);
            Journal($"Liste « {list.Name} » : {added} film(s) ajoutés à la liste de souhaits, {notFound} introuvables sur TMDb");
        });
    }

    /// <summary>Recalcule le Top 250 depuis les fichiers publics d'IMDb (bouton de Réglages &gt; Métadonnées).</summary>
    public async Task BuildImdbTopAsync()
    {
        if (_extras.Lists is not { } service || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var list = await service.BuildImdbTopAsync(progress: new Progress<string>(m => Status = m));
            SideBar.Reload();
            SideBar.ShowReferenceList(list.Id);
            Status = Tr.F("{0} : {1} films, {2} au catalogue", list.Name, list.Total, list.Owned);
            Journal($"Liste « {list.Name} » recalculée : {list.Total} films, {list.Owned} au catalogue");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or InvalidOperationException)
        {
            Status = Tr.F("Classement IMDb indisponible : {0}", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public IReadOnlyList<Choice<SortField>> Sorts { get; } =
    [
        new(SortField.Title, "Titre"),
        new(SortField.Year, "Année"),
        new(SortField.Number, "Numéro"),
        new(SortField.AddedAt, "Date d'ajout"),
        new(SortField.UserRating, "Note personnelle"),
        new(SortField.Runtime, "Durée"),
        new(SortField.ListOrder, "Ordre de la liste"),
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

    /// <summary>
    /// Les sous-menus du bandeau gauche : disques, listes de référence, parcours par genre, décennie, liste
    /// personnelle, pays et langue. Le bandeau tient ses listes et leur exclusion mutuelle ; quand une
    /// entrée est choisie, il le demande et la fenêtre applique la recherche.
    /// </summary>
    public SideBarViewModel SideBar { get; }

    private void OnSideBarRequested(SideBarRequest request)
    {
        switch (request.Source)
        {
            case SideBarSource.ReferenceList:
                Home = null;
                PeopleBrowser = null;
                break;
            case SideBarSource.Facet:
                PeopleBrowser = null;
                Home = null;
                ReleaseDuplicateGroup();

                // Une liste personnelle s'ouvre dans l'ordre que l'utilisateur lui a donné ; il reste libre de
                // trier autrement ensuite, le choix du tri l'emporte alors.
                if (request.ListName is not null && SelectedSort.Value != SortField.ListOrder)
                {
                    SelectedSort = Sorts.First(s => s.Value == SortField.ListOrder);
                }

                break;
        }

        _nextTrail = request is { GroupKey: SideBarViewModel.GenresKey, Label: { } genre, Rules: not null }
            ? new GalleryTrail(GalleryKind.Genres, Tr.T("Genres"), genre, string.Empty)
            : null;
        Rules = request.Rules;
        Trail = _nextTrail;
        _nextTrail = null;
        if (request.Rules is not null)
        {
            Gallery = null;
        }
    }

    /// <summary>
    /// La fiche de droite : le titre affiché et ce qu'on en montre. La fenêtre lui dit quel titre montrer ;
    /// les actions sur ce titre restent ici, parce qu'elles touchent à la liste et aux dialogues.
    /// </summary>
    public RecordViewModel Record { get; }

    private TitleDetail? Detail => Record.Detail;

    private IReadOnlyList<MediaFileEntry> DetailFiles => Record.DetailFiles;

    private string BorrowerText => Record.BorrowerText;

    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    /// <summary>Vue affiches (grille) ou liste détaillée.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsListView))]
    public partial bool IsGridView { get; set; }

    public bool IsListView => !IsGridView;

    private const string GridViewKey = "ui.gridView";

    // La vue choisie est mémorisée pour le catalogue.
    partial void OnIsGridViewChanged(bool value) => _media?.Settings.Set(GridViewKey, value ? "true" : "false");

    /// <summary>Largeur de la fiche (bandeau droit), réglée avec la poignée et mémorisée pour le catalogue.</summary>
    [ObservableProperty]
    public partial double DetailWidth { get; set; } = DefaultDetailWidth;

    public const double DefaultDetailWidth = 372;

    public const double MinDetailWidth = 300;

    public const double MaxDetailWidth = 760;

    /// <summary>Largeur de la fiche repliée : une bande étroite qui ne garde que le bouton pour la rouvrir.</summary>
    public const double CollapsedDetailWidth = 30;

    /// <summary>En deçà, un glissement de la poignée replie la fiche au lieu de la rétrécir.</summary>
    public const double CollapseThreshold = 200;

    private const string DetailWidthKey = "ui.detailWidth";

    private const string DetailCollapsedKey = "ui.detailCollapsed";

    /// <summary>
    /// Fiche repliée contre le bord droit : la zone centrale occupe presque toute la largeur, pour parcourir les
    /// affiches. Le bouton de la bande, la poignée ou F9 la rouvrent à sa largeur précédente.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailToggleTip))]
    public partial bool DetailCollapsed { get; set; }

    public string DetailToggleTip => DetailCollapsed
        ? Tr.T("Afficher la fiche du titre (F9)")
        : Tr.T("Replier la fiche pour élargir les affiches (F9)");

    partial void OnDetailCollapsedChanged(bool value)
    {
        if (_persistDetailCollapsed)
        {
            _media?.Settings.Set(DetailCollapsedKey, value ? "true" : "false");
        }
    }

    private bool _persistDetailCollapsed = true;

    /// <summary>
    /// Replie ou rouvre la fiche parce que la fenêtre a changé de largeur. Le réglage mémorisé n'est pas
    /// touché : c'est le choix de l'utilisateur, et il doit revenir quand la fenêtre retrouve de la place.
    /// </summary>
    public void CollapseDetailForWidth(bool collapsed)
    {
        _persistDetailCollapsed = false;
        try
        {
            DetailCollapsed = collapsed;
        }
        finally
        {
            _persistDetailCollapsed = true;
        }
    }

    [RelayCommand]
    private void ToggleDetail() => DetailCollapsed = !DetailCollapsed;

    partial void OnDetailWidthChanged(double value) =>
        _media?.Settings.Set(DetailWidthKey, value.ToString("0", CultureInfo.InvariantCulture));

    private static double ClampDetailWidth(string? stored) =>
        double.TryParse(stored, NumberStyles.Float, CultureInfo.InvariantCulture, out var width)
            ? Math.Clamp(width, MinDetailWidth, MaxDetailWidth)
            : DefaultDetailWidth;

    [RelayCommand]
    private void ShowGrid() => IsGridView = true;

    [RelayCommand]
    private void ShowList() => IsGridView = false;

    /// <summary>
    /// Les films de la collection d'un titre : ceux du catalogue, puis ceux que la fiche TMDb de la saga
    /// annonce et qu'on n'a pas. Rangés par année, comme une saga se regarde.
    /// </summary>
    private (IReadOnlyList<CollectionSibling> Films, string? Name) CollectionOf(TitleDetail title)
    {
        if (title.CollectionId is not { } collectionId || _metadata?.Collections is not { } service)
        {
            return ([], null);
        }

        var detail = service.Repository.Get(collectionId);
        var owned = service.Repository.Members(collectionId);
        var known = owned.Where(m => m.TmdbId is not null).Select(m => m.TmdbId!.Value).ToHashSet();
        var films = owned
            .Select(m => new CollectionSibling(m.TitleId, m.Title, m.Year, service.Resolve(m.PosterRelativePath), m.TitleId == title.Id))
            .Concat((detail?.Parts ?? [])
                .Where(p => !known.Contains(p.SourceId))
                .Select(p => new CollectionSibling(null, p.Title, p.Year, null, false)))
            .OrderBy(f => f.Year ?? int.MaxValue)
            .ThenBy(f => f.Title, StringComparer.CurrentCulture)
            .ToList();

        // Un seul film dans la saga, et aucun manquant : l'onglet n'apprendrait rien.
        return films.Count > 1 ? (films, detail?.Name ?? title.CollectionName) : ([], null);
    }

    private bool CanFetchSuggestions() =>
        _extras.Suggestions is not null && _metadata is not null && !IsBusy && !Record.SuggestionsFetched && Detail?.TmdbId is not null;

    /// <summary>Demande à TMDb ses suggestions pour la fiche affichée, une seule fois par titre.</summary>
    [RelayCommand(CanExecute = nameof(CanFetchSuggestions))]
    private async Task FetchSuggestionsAsync()
    {
        if (Detail is not { TmdbId: { } sourceId } detail || _extras.Suggestions is not { } store)
        {
            return;
        }

        await FetchSuggestionsForAsync(detail);
    }

    /// <summary>
    /// Demande les suggestions de TMDb pour un titre et les garde en base. À part de la commande, parce que
    /// l'onglet Recommandations de la fiche ouverte les demande pour le titre qu'il édite, pas pour l'affiché.
    /// </summary>
    private async Task FetchSuggestionsForAsync(TitleDetail title)
    {
        if (title.TmdbId is not { } sourceId || _extras.Suggestions is not { } store || _metadata is null)
        {
            return;
        }

        await RunMetadataAsync(async () =>
        {
            var found = await _metadata.RecommendationsAsync(title.Kind, sourceId, CancellationToken.None);
            store.Save(title.Id, found);
            Record.ReloadSuggestions();
            Status = found.Count == 0
                ? Tr.T("TMDb ne propose rien pour ce titre.")
                : Tr.F("{0} suggestion(s) de TMDb.", found.Count);
        });
    }

    /// <summary>Ouvre une suggestion déjà au catalogue, ou ajoute l'autre à la liste de souhaits.</summary>
    [RelayCommand]
    private async Task OpenSuggestionAsync(TmdbSuggestion? suggestion)
    {
        if (suggestion is null)
        {
            return;
        }

        if (suggestion.OwnedTitleId is { } owned)
        {
            GoToTitle(owned);
            return;
        }

        await AddSuggestionToWishlistAsync(suggestion);
    }

    /// <summary>
    /// Un film suggéré qu'on n'a pas : sa fiche est rapatriée de TMDb et il entre en liste de souhaits. La
    /// question est posée avant, puisque chaque ajout coûte un appel et une entrée au catalogue.
    /// </summary>
    private async Task AddSuggestionToWishlistAsync(TmdbSuggestion suggestion)
    {
        if (_metadata is null || !await _dialogs.ConfirmAsync(
                "Ajouter à la liste de souhaits",
                Tr.F("Ajouter « {0} » à votre liste de souhaits ? Sa fiche est rapatriée de TMDb, et il est marqué non possédé.", suggestion.Name)))
        {
            return;
        }

        await RunMetadataAsync(async () =>
        {
            var result = new MetadataSearchResult(suggestion.Kind, suggestion.TmdbId, suggestion.Name, null, suggestion.Year, null, suggestion.PosterPath, suggestion.Rating);
            var id = await _metadata.ImportAsync(result, ImportOptions.Default);
            _titles.SetOwned([id], false);
            Record.ReloadSuggestions();
            Refresh(selectId: id);
            Status = Tr.F("« {0} » ajouté à la liste de souhaits.", suggestion.Name);
            Journal($"Ajouté à la liste de souhaits depuis les suggestions TMDb : {suggestion.Name} (TMDb {suggestion.TmdbId})");
        });
    }

    /// <summary>Ouvre la fiche d'un titre proposé par « Vous aimerez aussi ».</summary>
    [RelayCommand]
    private void OpenSimilar(SimilarTitle? similar)
    {
        if (similar is not null)
        {
            GoToTitle(similar.Title.Id);
        }
    }

    public bool HasPeople => _media?.People is not null;

    /// <summary>Fiche d'une personne ; si l'un de ses titres y est choisi, il est sélectionné dans le catalogue.</summary>
    [RelayCommand]
    private async Task OpenPersonAsync(CastMember? member)
    {
        if (member is null || _media?.People is not { } people)
        {
            return;
        }

        var page = new PersonViewModel(people, member.PersonId, member.Role == "director" ? "director" : null, _media?.Export);
        if (await _dialogs.ShowPersonAsync(page) && page.ChosenTitleId is { } titleId)
        {
            GoToTitle(titleId);
        }
    }

    /// <summary>
    /// Galerie des acteurs ou des réalisateurs, affichée dans la zone centrale à la place des affiches
    /// (liens du bandeau gauche) ; les filtres de la bibliothèque ramènent les affiches.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPeopleView), nameof(IsTitlesView), nameof(IsActorsView), nameof(IsDirectorsView))]
    public partial PeopleBrowserViewModel? PeopleBrowser { get; private set; }

    public bool IsPeopleView => PeopleBrowser is not null;

    public bool IsTitlesView => PeopleBrowser is null && Home is null && Gallery is null;

    /// <summary>
    /// Galerie affichée dans la vue centrale : les collections ou les genres, en vignettes. Entrer dans une
    /// vignette revient à la liste des titres, filtrée, avec un fil d'Ariane pour revenir ici.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGalleryView), nameof(IsTitlesView), nameof(IsCollectionsView))]
    public partial GalleryViewModel? Gallery { get; private set; }

    public bool IsGalleryView => Gallery is not null;

    /// <summary>Le lien « Collections » du bandeau est allumé : on est dans leur galerie, ou dans l'une d'elles.</summary>
    public bool IsCollectionsView => Gallery is { Kind: GalleryKind.Collections } || Trail is { Kind: GalleryKind.Collections };

    partial void OnGalleryChanged(GalleryViewModel? value)
    {
        if (value is not null)
        {
            Home = null;
            PeopleBrowser = null;
        }
    }

    /// <summary>Où l'on est entré depuis une galerie ; nul quand la liste ne vient pas de là.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTrail), nameof(IsCollectionsView), nameof(IsInCollection))]
    public partial GalleryTrail? Trail { get; private set; }

    public bool HasTrail => Trail is not null;

    public bool IsInCollection => Trail is { Kind: GalleryKind.Collections };

    /// <summary>Le fil d'Ariane à poser quand la recherche en cours de changement sera appliquée.</summary>
    private GalleryTrail? _nextTrail;

    /// <summary>Montre la galerie des collections dans la vue centrale.</summary>
    [RelayCommand]
    private void ShowCollectionGallery()
    {
        if (_metadata?.Collections is not { } service)
        {
            return;
        }

        var cards = service.Repository.List()
            .Select(c =>
            {
                // Le décor de la saga fait le meilleur visuel ; à défaut son affiche ; à défaut la couleur seule.
                var detail = service.Repository.Get(c.Id);
                var image = service.Resolve(detail?.BackdropRelativePath) ?? service.Resolve(c.PosterRelativePath);
                var count = c.Parts > 0 ? Tr.F("{0} / {1} films", c.Owned, c.Parts) : Tr.F("{0} film(s)", c.Owned);
                return new GalleryCard(GalleryKind.Collections, c.Name, c.Name, count, image is null ? [] : [image], c.Id);
            })
            .ToList();
        ShowGallery(new GalleryViewModel(
            GalleryKind.Collections,
            Tr.T("Collections"),
            Tr.F("{0} collection(s). Cliquez sur l'une d'elles pour voir ses films.", cards.Count),
            cards));
    }

    /// <summary>Montre la galerie des genres dans la vue centrale.</summary>
    [RelayCommand]
    private void ShowGenreGallery()
    {
        var cards = _titles.GenreShowcases()
            .Select(g => new GalleryCard(
                GalleryKind.Genres,
                g.Code,
                Tr.T(g.Label),
                Tr.F("{0} titre(s)", g.Titles),
                g.PosterRelativePaths.Select(p => _images?.PathInside(p)).OfType<string>().ToList()))
            .OrderBy(c => c.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        ShowGallery(new GalleryViewModel(
            GalleryKind.Genres,
            Tr.T("Genres"),
            Tr.F("{0} genre(s). Cliquez sur l'un d'eux pour voir ses titres.", cards.Count),
            cards));
    }

    private void ShowGallery(GalleryViewModel gallery)
    {
        // Une galerie n'est pas une recherche : ce que le bandeau avait de choisi est relâché, et la liste
        // qu'on retrouvera en sortant est entière (elle se relit en arrière-plan, derrière la galerie).
        SideBar.ReleaseAll();
        ReleaseDuplicateGroup();
        Rules = null;
        Trail = null;
        Gallery = gallery;
    }

    /// <summary>Entre dans une vignette : ses films s'affichent dans la liste, avec de quoi revenir.</summary>
    [RelayCommand]
    private void OpenGalleryCard(GalleryCard? card)
    {
        if (card is null)
        {
            return;
        }

        if (card.Kind == GalleryKind.Genres)
        {
            // Par le bandeau : c'est lui qui applique la recherche, et son sous-menu montre où l'on est.
            if (!SideBar.SelectFacet(SideBarViewModel.GenresKey, i => i.Search.Rules.Any(r => r.Value == card.Key)))
            {
                ShowInTitles(new RuleSearch([new SearchRule("genre", RuleOperator.Equals, card.Key)]), TrailOf(card));
            }

            return;
        }

        SideBar.ReleaseAll();
        ReleaseDuplicateGroup();
        ShowInTitles(new RuleSearch([new SearchRule("collection", RuleOperator.Equals, card.Key)]), TrailOf(card));
    }

    private static GalleryTrail TrailOf(GalleryCard card) => new(
        card.Kind,
        card.Kind == GalleryKind.Collections ? Tr.T("Collections") : Tr.T("Genres"),
        card.Title,
        card.Subtitle,
        card.Id);

    /// <summary>Affiche la liste des titres pour cette recherche, sous ce fil d'Ariane.</summary>
    private void ShowInTitles(RuleSearch rules, GalleryTrail trail)
    {
        _nextTrail = trail;
        Rules = rules;

        // Si la recherche était déjà celle-là, rien n'a changé et rien n'a posé le fil : on le pose ici.
        _nextTrail = null;
        Trail = trail;
        Gallery = null;
    }

    /// <summary>Revient à la galerie d'où l'on est entré.</summary>
    [RelayCommand]
    private void BackToGallery()
    {
        if (Trail is not { } trail)
        {
            return;
        }

        if (trail.Kind == GalleryKind.Collections)
        {
            ShowCollectionGallery();
        }
        else
        {
            ShowGenreGallery();
        }
    }


    public bool IsActorsView => PeopleBrowser is { IsDirectors: false };

    public bool IsDirectorsView => PeopleBrowser is { IsDirectors: true };

    /// <summary>Réalisateurs du catalogue dans la zone centrale (lien du bandeau gauche).</summary>
    [RelayCommand]
    private void OpenDirectors() => ShowPeople("director");

    public bool ShowDirectorsLink => HasPeople && _display.ShowDirectors;

    /// <summary>Acteurs et équipe du catalogue dans la zone centrale (lien du bandeau gauche).</summary>
    [RelayCommand]
    private void OpenPeople() => ShowPeople(null);

    private void ShowPeople(string? role)
    {
        if (_media?.People is not { } people || (PeopleBrowser is { } current && current.Role == role))
        {
            return;
        }

        PeopleBrowser = new PeopleBrowserViewModel(people, role);
    }

    /// <summary>Revient aux affiches des titres (filtres du haut du bandeau gauche).</summary>
    [RelayCommand]
    private void ShowTitles() => PeopleBrowser = null;

    /// <summary>
    /// Page d'accueil : à voir ce soir, sagas commencées, derniers ajouts, oubliés. Elle occupe le panneau
    /// central comme la galerie des personnes, et toute vue de la bibliothèque la referme.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHomeView), nameof(IsTitlesView))]
    public partial HomeViewModel? Home { get; private set; }

    public bool IsHomeView => Home is not null;

    public bool CanShowHome => _extras.Home is not null;

    [RelayCommand]
    private void ShowHome()
    {
        if (_extras.Home is not { } home)
        {
            return;
        }

        PeopleBrowser = null;
        var page = new HomeViewModel();
        // Les disques montés viennent du dépôt des fichiers : « lisible maintenant » dépend de ce qui est branché.
        var mounted = (_media?.Files.VolumeProvider ?? Volumes.Current)().Labels;
        page.Load(home, Ordered, mounted, _extras.Statistics?.Compute());
        Home = page;
    }

    /// <summary>Fiches de ces identifiants, dans l'ordre demandé : la requête de liste ne le garde pas.</summary>
    private IReadOnlyList<TitleSummary> Ordered(IReadOnlyList<long> ids)
    {
        var rows = _titles.Query(new TitleQuery(Ids: ids)).Select(Prepare).ToDictionary(t => t.Id);
        return ids.Where(rows.ContainsKey).Select(id => rows[id]).ToList();
    }

    /// <summary>
    /// Film cliqué sur la page d'accueil : la liste revient, placée sur lui (la sélection reste derrière),
    /// et sa fiche s'ouvre. Avant, le clic ne faisait que ramener à la liste.
    /// </summary>
    [RelayCommand]
    private async Task OpenFromHomeAsync(TitleSummary? title)
    {
        if (title is null)
        {
            return;
        }

        Home = null;

        // GoToTitle peut relire la liste (Refresh) : la sélection est posée là, de façon synchrone, avant la
        // fiche. La fiche s'ouvre par l'identifiant cliqué, pas par la sélection, au cas où la liste l'aurait perdue.
        GoToTitle(title.Id);
        await EditTitleAsync(title.Id);
    }

    /// <summary>
    /// Clic sur une vue de la bibliothèque : on quitte la galerie des personnes et le disque choisi, même quand
    /// la vue cliquée était déjà celle en cours (le filtre ne change pas, mais l'intention est de tout revoir).
    /// </summary>
    public void LeaveSideViews()
    {
        Home = null;
        PeopleBrowser = null;
        Gallery = null;
        Trail = null;
        SideBar.ReleaseAll();
        ReleaseDuplicateGroup();
        Rules = null;
    }

    partial void OnPeopleBrowserChanged(PeopleBrowserViewModel? value)
    {
        PersonPage = null;
        if (value is not null)
        {
            Home = null;
            Gallery = null;
        }
    }

    partial void OnHomeChanged(HomeViewModel? value)
    {
        if (value is not null)
        {
            Gallery = null;
        }
    }

    /// <summary>Personne choisie dans la galerie : ses informations remplacent la fiche du titre dans le bandeau droit.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPersonPage), nameof(SelectedPersonIds))]
    public partial PersonViewModel? PersonPage { get; private set; }

    public bool HasPersonPage => PersonPage is not null;

    /// <summary>Personne sélectionnée, pour marquer sa carte dans la galerie (virtualisée).</summary>
    public IReadOnlySet<long> SelectedPersonIds => PersonPage is { } page ? new HashSet<long> { page.PersonId } : new HashSet<long>();

    /// <summary>Clic sur une personne de la galerie : bandeau droit, complété depuis TMDb si la fiche manque.</summary>
    [RelayCommand]
    private async Task SelectPersonAsync(PersonSummary? person)
    {
        if (person is null || PeopleBrowser is not { } browser || PersonPage?.PersonId == person.Id)
        {
            return;
        }

        var page = new PersonViewModel(browser.Service, person.Id, browser.Role, _media?.Export);
        PersonPage = page;
        try
        {
            await page.LoadDetailsAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            // Hors ligne : le bandeau garde ce que le catalogue connaît déjà.
        }
    }

    /// <summary>Film choisi dans le bandeau d'une personne : retour aux affiches, sur ce film.</summary>
    [RelayCommand]
    private void ChoosePersonTitle(PersonCredit? credit)
    {
        if (credit is null)
        {
            return;
        }

        PeopleBrowser = null;
        GoToTitle(credit.TitleId);
    }

    /// <summary>Double-clic sur une personne : sa fiche complète ; si l'un de ses titres y est choisi, retour aux affiches sur ce titre.</summary>
    [RelayCommand]
    private async Task OpenBrowsedPersonAsync(PersonSummary? person)
    {
        if (person is null || PeopleBrowser is not { } browser)
        {
            return;
        }

        var page = new PersonViewModel(browser.Service, person.Id, browser.Role, _media?.Export);
        if (await _dialogs.ShowPersonAsync(page) && page.ChosenTitleId is { } titleId)
        {
            PeopleBrowser = null;
            GoToTitle(titleId);
        }
    }

    /// <summary>Sélectionne un titre, en levant recherche et filtres s'il n'est pas affiché.</summary>
    public void GoToTitle(long titleId)
    {
        if (Titles.FirstOrDefault(t => t.Id == titleId) is { } shown)
        {
            // Déjà sous les yeux : rien n'a changé dans la base, le sélectionner suffit.
            SetSelection([shown]);
            return;
        }

        _loaded = false;
        SearchText = string.Empty;
        SelectedFilter = Filters[0];
        ReleaseDuplicateGroup();
        Rules = null;
        _loaded = true;
        Refresh(selectId: titleId);
    }

    public bool HasMedia => _media is not null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddFromTmdbCommand), nameof(LinkToTmdbCommand), nameof(UpdateAllFromTmdbCommand))]
    public partial bool IsBusy { get; set; }

    /// <summary>Progression du travail en cours : nombre d'étapes faites et total (0 : durée inconnue, barre animée).</summary>
    [ObservableProperty]
    public partial int BusyDone { get; private set; }

    [ObservableProperty]
    public partial int BusyTotal { get; private set; }

    public bool IsBusyIndeterminate => IsBusy && BusyTotal == 0;

    partial void OnIsBusyChanged(bool value)
    {
        if (value)
        {
            BusyDone = 0;
            BusyTotal = 0;
        }

        OnPropertyChanged(nameof(IsBusyIndeterminate));
    }

    partial void OnBusyTotalChanged(int value) => OnPropertyChanged(nameof(IsBusyIndeterminate));

    /// <summary>Progression affichée dans la barre : chaque signalement compte une étape sur le total annoncé.</summary>
    private Progress<string> TrackedProgress(int total)
    {
        BusyDone = 0;
        BusyTotal = total;
        var done = 0;
        return new Progress<string>(message =>
        {
            Status = message;
            BusyDone = Math.Min(++done, total);
        });
    }

    public bool HasMetadataSource => _metadata is not null;

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

    partial void OnRulesChanged(RuleSearch? value)
    {
        // Une recherche qui change, c'est la liste des titres qu'on regarde : la galerie s'efface, et le fil
        // d'Ariane ne reste que si c'est lui qui a amené cette recherche.
        Trail = _nextTrail;
        Gallery = null;
        RefreshView();
    }

    private TitleQuery CurrentQuery => new(SearchText, SelectedFilter.Value, SelectedSort.Value, SortDescending, _display.HideAdult,
        Rules: Rules, Ids: _duplicateIds, GroupOrder: SideBar.SelectedListName);

    private IReadOnlyList<long> SelectedIds => SelectedTitles.Select(t => t.Id).ToList();

    public void Load()
    {
        _loaded = true;
        IsGridView = _media?.Settings.Get(GridViewKey) == "true";
        DetailWidth = ClampDetailWidth(_media?.Settings.Get(DetailWidthKey));
        DetailCollapsed = _media?.Settings.Get(DetailCollapsedKey) == "true";
        ReloadSideLists();
        Refresh();
    }

    /// <summary>
    /// Recharge les listes du bandeau gauche (disques et sous-menus de parcours). Volontairement hors de
    /// <see cref="Refresh"/>, qui s'exécute à chaque changement de filtre ou de tri : ces listes ne bougent
    /// qu'après un import, une mise à jour TMDb ou une opération de maintenance.
    /// </summary>
    private void ReloadSideLists()
    {
        SideBar.Reload();
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
            RefreshView();
            return;
        }

        if (_searchTimer is null)
        {
            _searchTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _searchTimer.Tick += async (_, _) =>
            {
                _searchTimer.Stop();
                await SearchAsync();
            };
        }

        _searchTimer.Stop();
        _searchTimer.Start();
    }

    partial void OnSelectedFilterChanged(FilterItem value)
    {
        if (SideBar.ReleaseDisc())
        {
            // Choisir une vue de la bibliothèque sort du disque, comme elle sort d'une recherche multicritère.
            Rules = null;
        }

        SideBar.ReleaseBrowsing();
        Home = null;
        Gallery = null;
        Trail = null;
        ReleaseDuplicateGroup();

        OnPropertyChanged(nameof(SelectedMainFilter));
        OnPropertyChanged(nameof(SelectedDefinitionFilter));
        if (DefinitionFilters.Contains(value))
        {
            IsDefinitionExpanded = true;
        }

        RefreshView();
    }

    partial void OnSelectedSortChanged(Choice<SortField> value)
    {
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
        RefreshView();
    }

    partial void OnSortDescendingChanged(bool value) => RefreshView();

    /// <summary>Plusieurs titres sélectionnés : le bandeau droit affiche leur nombre et leur liste.</summary>
    public bool HasMultiSelection => SelectedTitles.Count > 1;

    /// <summary>Un seul ou aucun titre, sans fiche : invite du bandeau droit.</summary>
    public bool ShowDetailHint => Detail is null && SelectedTitles.Count <= 1;

    public string SelectionLabel => Tr.F("{0} titres sélectionnés", SelectedTitles.Count);

    /// <summary>Titres sélectionnés montrés dans le bandeau droit (bornés, pour rester léger).</summary>
    public IReadOnlyList<string> SelectionPreview
    {
        get
        {
            const int cap = 60;
            List<string> preview = [.. SelectedTitles.Take(cap).Select(t => t.Title)];
            if (SelectedTitles.Count > cap)
            {
                preview.Add(Tr.F("… et {0} autre(s)", SelectedTitles.Count - cap));
            }

            return preview;
        }
    }

    /// <summary>Croix d'une affiche sélectionnée : retire ce titre de la sélection.</summary>
    [RelayCommand]
    private void RemoveFromSelection(TitleSummary? title)
    {
        if (title is not null)
        {
            SelectedTitles.Remove(title);
        }
    }

    private void OnSelectionChanged()
    {
        Record.Show(SelectedTitles.Count == 1 ? SelectedTitles[0].Id : null);
        OnPropertyChanged(nameof(HasMultiSelection));
        OnPropertyChanged(nameof(ShowDetailHint));
        OnPropertyChanged(nameof(SelectionLabel));
        OnPropertyChanged(nameof(SelectionPreview));
        LinkToTmdbCommand.NotifyCanExecuteChanged();
        OpenSeriesCommand.NotifyCanExecuteChanged();
        EditCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        ToggleSeenCommand.NotifyCanExecuteChanged();
        ToggleFavoriteCommand.NotifyCanExecuteChanged();
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
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

    /// <summary>
    /// Vide la fiche de tout ce qui vient d'une source extérieure (descriptif, identifiants, notes des sites,
    /// genres, distribution, affiche) ; le titre, ce que l'utilisateur a saisi et les fichiers restent.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task ClearDetailAsync()
    {
        var targets = SelectedTitles.Select(t => (t.Id, t.Title)).ToList();
        if (targets.Count == 0 || !await _dialogs.ConfirmAsync(
                "Vider la fiche",
                Tr.F("Effacer le descriptif, les identifiants, les notes des sites, les genres, la distribution et l'affiche de {0} ?\nLe titre, vos propres saisies (note, tags, groupes, commentaires, vu, prêts) et les fichiers vidéo avec leurs infos MediaInfo sont gardés.",
                    targets.Count == 1 ? Tr.F("« {0} »", targets[0].Title) : Tr.F("{0} titres", targets.Count))))
        {
            return;
        }

        foreach (var (id, _) in targets)
        {
            _titles.ClearMetadata(id);
        }

        Journal($"Fiche(s) vidée(s) : {Names(SelectedTitles.ToList())}");
        Status = targets.Count == 1
            ? Tr.F("Fiche de « {0} » vidée : relancez « Lier ou mettre à jour depuis TMDb » pour la remplir.", targets[0].Title)
            : Tr.F("{0} fiches vidées : relancez « Mettre à jour via TMDb » pour les remplir.", targets.Count);
        Touch(targets.Select(t => t.Id).ToList());
    }

    /// <summary>Un titre : fiche complète. Plusieurs : modification groupée.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task EditAsync()
    {
        if (SelectedTitles.Count > 1)
        {
            await BulkEditAsync();
            return;
        }

        if (SelectedTitles.Count == 1)
        {
            await EditTitleAsync(SelectedTitles[0].Id);
        }
    }

    /// <summary>
    /// Ouvre la fiche de ce titre. Elle ne dépend pas de la sélection : depuis l'Accueil, la liste derrière
    /// vient d'être relue, et c'est le titre cliqué qu'on ouvre, quoi qu'elle ait gardé de sa sélection.
    /// </summary>
    private async Task EditTitleAsync(long titleId)
    {
        // Un double-clic ouvre la fiche avant la fin du délai de chargement : le générique doit y être.
        Record.EnsureLoaded();

        var current = _titles.Get(titleId);
        if (current is null)
        {
            return;
        }

        // La fiche reprend tout ce que montre le panneau de droite : affiche, notes, distribution, fichiers.
        var (collectionFilms, collectionName) = CollectionOf(current);
        TitleEditorViewModel? editorRef = null;
        var extras = new TitleEditorExtras(
            _images?.PathFor(current.Id, "poster"),
            Detail?.Id == current.Id ? Record.Cast.ToList() : null,
            Detail?.Id == current.Id ? Record.CrewText : null,
            Detail?.Id == current.Id ? Record.Directors.ToList() : null,
            Record.MediaTextOf(current.Id),
            _media is null ? null : async () =>
            {
                var (_, tool) = await _media.Import.RefreshTechnicalAsync([current.Id], new Progress<string>(m => Status = m), CancellationToken.None);
                Status = tool == "aucun" ? "MediaInfo introuvable : indiquez son chemin dans les réglages." : Tr.F("Infos techniques relues avec {0}", tool);
                return Record.MediaTextOf(current.Id);
            },
            _media is null ? null : async path =>
            {
                var part = await _media.Import.AddPartAsync(current.Id, path, videoExtensions: ScanSettings.Load(_media.Settings).ExtensionSet);
                Status = Tr.F("Partie {0} rattachée à « {1} »", part, current.Title);
                Journal($"Partie {part} rattachée à « {current.Title} » : {path}");
                return Record.MediaTextOf(current.Id);
            },
            _media?.People,
            new LoanEditorViewModel(_titles, current.Id),
            _media?.Files,
            _images is null ? null : path => Task.Run(() => _images.SetFromFile(current.Id, path, "poster")),
            _images is null ? null : () =>
            {
                _images.Remove(current.Id, "poster");
                return Task.CompletedTask;
            },
            _metadata is null ? null : async (TmdbReference reference) =>
            {
                // La fiche suit la référence affichée dans l'éditeur : la changer puis mettre à jour la
                // rattache. « KeepKind » garde le titre tel qu'il est : un téléfilm pris dans la section
                // « tv » de TMDb reste un film au catalogue.
                var applied = await _metadata.UpdateAsync(
                    current.Id, reference.Kind, reference.Id, ImportOptions.Default with { KeepKind = true });
                Journal(applied
                    ? $"Fiche mise à jour depuis TMDb : {current.Title} (TMDb {reference.Text})"
                    : $"Titre verrouillé, non modifié : {current.Title}");
                return applied ? _titles.Get(current.Id) : null;
            },
            new ViewingsViewModel(_titles, current.Id),
            () =>
            {
                var (cast, directors, crew) = Record.CreditsOf(current.Id);
                return new TitleEditorView(cast, directors, crew, _images?.PathFor(current.Id, "poster"), _titles.Collections());
            },
            collectionFilms,
            collectionName,
            Record.SimilarOf(current.Id),
            _extras.Suggestions?.For(current.Id) ?? [],
            _extras.Suggestions is null || _metadata is null || current.TmdbId is null
                ? null
                : async () =>
                {
                    await FetchSuggestionsForAsync(current);
                    return _extras.Suggestions.For(current.Id);
                },
            _extras.Suggestions is null || _metadata is null ? null : suggestion => AddSuggestionToWishlistAsync(suggestion),
            NeighbourOf(current.Id, -1),
            NeighbourOf(current.Id, 1),
            // Le chemin de lecture du bandeau droit, tel quel : disque à brancher, autres lecteurs, lecteur
            // externe, journal et historique. La question « branchez le disque » s'ouvre devant la fiche.
            Play: _media is null ? null : () => PlayTitleAsync(current.Id, keepSelection: null, ask: editorRef?.Ask));
        var editor = editorRef = new TitleEditorViewModel(
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

            Touch([updated.Id]);
        }
        else if (editor.NavigateToTitleId is null)
        {
            // Infos techniques relues, partie ajoutée ou prêt modifié depuis la fiche, même sans enregistrer.
            Touch([current.Id]);
        }

        if (editor.NavigateToTitleId is { } target)
        {
            // Un film choisi depuis la fiche (acteur, collection, recommandations), ou le titre voisin.
            GoToTitle(target);
            if (editor.ReopenAfterNavigation && SelectedTitles.Count == 1 && SelectedTitles[0].Id == target)
            {
                // Enchaîner les fiches : la suivante s'ouvre aussitôt, sans repasser par la liste.
                await EditAsync();
            }
        }
    }

    /// <summary>Le titre voisin dans la liste affichée, ou rien au bout de la liste.</summary>
    private long? NeighbourOf(long titleId, int step)
    {
        for (var i = 0; i < Titles.Count; i++)
        {
            if (Titles[i].Id == titleId)
            {
                var index = i + step;
                return index >= 0 && index < Titles.Count ? Titles[index].Id : null;
            }
        }

        return null;
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
        RefreshTitles(ids);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void ToggleFavorite()
    {
        var ids = SelectedIds;
        var favorite = SelectedTitles.Any(t => !t.IsFavorite);
        _titles.SetFavorite(ids, favorite);
        Journal($"{(favorite ? "Ajouté aux" : "Retiré des")} favoris : {Names(SelectedTitles.ToList())}");
        RefreshTitles(ids);
    }

    [RelayCommand]
    private void PickRandom()
    {
        // Tiré dans la liste affichée, qui est déjà la réponse à la requête courante : la redemander à la
        // base puis tout relire coûtait deux lectures complètes du catalogue pour choisir un seul titre.
        var unseen = Titles.Where(t => !t.IsSeen).ToList();
        if (unseen.Count == 0)
        {
            Status = "Aucun titre non vu dans la sélection courante.";
            return;
        }

#pragma warning disable CA5394 // Tirage d'agrément, pas d'usage cryptographique.
        var pick = unseen[Random.Shared.Next(unseen.Count)];
#pragma warning restore CA5394
        SetSelection([pick]);
        Status = Tr.F("Au hasard : {0}", pick.Title);
    }

    [RelayCommand(CanExecute = nameof(CanLend))]
    private void Lend()
    {
        try
        {
            _titles.Lend(Detail!.Id, BorrowerText, DateTimeOffset.Now);
            Journal($"Prêté à {BorrowerText.Trim()} : {Detail.Title}");
            RefreshTitles([Detail.Id]);
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
        RefreshTitles([Detail.Id]);
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
            var id = await _metadata!.ImportAsync(choice, ImportOptions.Default);
            NewTitleText = string.Empty;
            Refresh(selectId: id);
            Status = Tr.F("« {0} » ajouté depuis TMDb", choice.Title);
            Journal($"Ajouté depuis TMDb : {choice.Title} (TMDb {choice.SourceId})");
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
        // Une fiche déjà liée se rafraîchit depuis sa propre référence, section comprise : un téléfilm
        // rattaché à « tv » doit être redemandé à « tv », pas à « movie ».
        var reference = detail.Tmdb;
        var keepKind = reference is not null;
        if (reference is null)
        {
            var query = detail.Year is { } y ? $"{detail.Title} ({y})" : detail.Title;
            var search = new MetadataSearchViewModel(_metadata!, query);
            if (await _dialogs.SearchMetadataAsync(search) is not { } choice)
            {
                return;
            }

            // Un choix fait dans la recherche décide aussi du genre : l'utilisateur a vu ce qu'il prenait.
            reference = new TmdbReference(choice.Kind, choice.SourceId);
        }

        await RunMetadataAsync(async () =>
        {
            var applied = await _metadata!.UpdateAsync(
                detail.Id, reference.Kind, reference.Id, ImportOptions.Default with { KeepKind = keepKind });
            Touch([detail.Id]);
            Status = applied ? "Fiche mise à jour depuis TMDb" : "Titre verrouillé : non modifié";
            Journal(applied ? $"Fiche mise à jour depuis TMDb : {detail.Title} (TMDb {reference.Text})" : $"Titre verrouillé, non modifié : {detail.Title}");
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
        await UpdateViaTmdbAsync(ids, scope);
    }

    /// <summary>Mise à jour TMDb des seuls titres sans fiche TMDb (bouton de Réglages > Métadonnées).</summary>
    public async Task UpdateUnlinkedFromTmdbAsync()
    {
        if (_metadata is null || IsBusy)
        {
            return;
        }

        var ids = _titles.WithoutMetadataIds();
        if (ids.Count == 0)
        {
            Status = Tr.T("Tous les titres ont déjà leur synopsis et leurs genres.");
            return;
        }

        await UpdateViaTmdbAsync(ids, Tr.F("les {0} titre(s) sans synopsis ni genre", ids.Count));
    }

    /// <summary>
    /// Passe de rattrapage (boutons de Réglages &gt; Métadonnées) : une mise à jour TMDb ordinaire restreinte
    /// aux fiches auxquelles il manque quelque chose. Rien de neuf à appeler, le détail complet suffit.
    /// </summary>
    public async Task UpdateMissingAsync(Func<IReadOnlyList<long>> missing, Func<int, string> scope, string nothingToDo)
    {
        ArgumentNullException.ThrowIfNull(missing);
        ArgumentNullException.ThrowIfNull(scope);
        if (_metadata is null || IsBusy)
        {
            return;
        }

        var ids = missing();
        if (ids.Count == 0)
        {
            Status = nothingToDo;
            return;
        }

        await UpdateViaTmdbAsync(ids, scope(ids.Count));
    }

    private async Task UpdateViaTmdbAsync(IReadOnlyList<long> ids, string scope)
    {
        if (ids.Count == 0 || !await _dialogs.ConfirmAsync(
                "Mettre à jour via TMDb",
                Tr.F("Mettre à jour {0} depuis TMDb ?\nLes fiches existantes sont complétées sur place, aucune entrée n'est créée. Les titres verrouillés sont ignorés.", scope)))
        {
            return;
        }

        await RunMetadataAsync(async () =>
        {
            var targets = ids.Select(_titles.Get).OfType<TitleDetail>().ToList();
            var progress = TrackedProgress(targets.Count);
            var result = await _metadata!.UpdateTitlesAsync(targets, ImportOptions.Default, progress, CancellationToken.None);
            ReloadSideLists();
            Touch(ids);
            Status = Tr.F("{0} mis à jour, {1} à vérifier, {2} non trouvé(s), {3} ignoré(s), {4} échec(s)", result.Updated, result.ToReview, result.NotFound, result.Skipped, result.Failed);
            Journal($"Mise à jour via TMDb de {targets.Count} titre(s) : {result.Updated} mis à jour, {result.ToReview} à vérifier, {result.NotFound} non trouvé(s), {result.Skipped} ignoré(s), {result.Failed} échec(s)");
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

    /// <summary>Exporte la liste telle qu'elle est affichée : ouvre les réglages sur l'onglet Export.</summary>
    [RelayCommand]
    private Task ExportListAsync() => ShowSettingsAsync(startOnExport: true);

    /// <summary>Exporte la seule sélection : même fenêtre, mais la portée est déjà choisie.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task ExportSelectionAsync() => ShowSettingsAsync(startOnExport: true, exportSelection: true);

    [RelayCommand]
    private Task OpenSettingsAsync() => ShowSettingsAsync(startOnExport: false);

    private async Task ShowSettingsAsync(bool startOnExport, bool exportSelection = false)
    {
        if (_settings is not null)
        {
            var settings = _settings();
            settings.StartOnExport = startOnExport;
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

            if (settings.Export is { } export)
            {
                var sort = SelectedSort;
                var descending = SortDescending;
                export.DisplayedIds = () => Titles.Select(t => t.Id).ToList();
                export.SelectedIds = () => SelectedIds;
                if (exportSelection)
                {
                    export.StartOnSelection();
                }
                export.AllIds = () => _titles.Query(new TitleQuery(null, QuickFilter.All, sort.Value, descending)).Select(t => t.Id).ToList();
            }

            var saved = await _dialogs.EditSettingsAsync(settings);
            if (settings.Backups?.PendingRestore is { } restore && _catalogs is not null)
            {
                // Le catalogue est fermé, remplacé par la sauvegarde puis rouvert : la fenêtre reçoit un nouveau contexte.
                _catalogs.RestoreCurrent(restore);
                return;
            }

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
                ReloadSideLists();
                Refresh(selectIds: SelectedIds);
            }

            if (maintenance?.PendingTitleId is { } titleId)
            {
                GoToTitle(titleId);
            }

            // Boutons de l'onglet Métadonnées : la fenêtre vient de se fermer, la passe démarre ici.
            switch (settings.Pending)
            {
                case BulkUpdate.Unlinked:
                    await UpdateUnlinkedFromTmdbAsync();
                    break;
                case BulkUpdate.Trailers:
                    await UpdateMissingAsync(
                        _titles.WithoutTrailerIds,
                        n => Tr.F("les {0} titre(s) sans bande-annonce", n),
                        Tr.T("Toutes les fiches liées à TMDb ont leur bande-annonce."));
                    break;
                case BulkUpdate.ImdbTop:
                    await BuildImdbTopAsync();
                    break;
                case BulkUpdate.Keywords:
                    await UpdateMissingAsync(
                        _titles.WithoutKeywordIds,
                        n => Tr.F("les {0} titre(s) sans mot-clé", n),
                        Tr.T("Toutes les fiches liées à TMDb ont leurs mots-clés."));
                    break;
                default:
                    break;
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
        catch (Exception ex) when (ex is TmdbUnavailableException or KeyNotFoundException or HttpRequestException or TaskCanceledException)
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

            ReloadSideLists();
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
    private Task PlayAsync() => Detail is { } detail ? PlayTitleAsync(detail.Id, keepSelection: null) : Task.CompletedTask;

    /// <summary>
    /// Retrouve le fichier d'un titre avant de s'en servir. S'il n'est pas là, on demande de connecter son
    /// disque (son MediaID est nommé), avec de quoi réessayer ou chercher le même chemin sur les autres
    /// lecteurs : la lettre d'un disque USB change d'un branchement à l'autre. Nul si l'utilisateur renonce.
    /// </summary>
    private async Task<LocatedMedia?> LocateFileAsync(long titleId, MediaLocator.Ask? ask = null)
    {
        if (_media is not { } media)
        {
            return null;
        }

        // Relu à chaque essai : un disque qu'on vient de brancher est reconnu à son nom, quelle que soit sa lettre.
        var located = await MediaLocator.LocateAsync(
            () => media.Files.FilesOf(titleId).Select(f => new MediaCandidate(f.FullPath, f.VolumeLabel, f.Id)).ToList(),
            ask ?? _dialogs.ChooseAsync,
            MediaRoots);
        if (located is { FoundElsewhere: true })
        {
            // Trouvé ailleurs : on s'en sert pour cette fois. L'emplacement enregistré au catalogue ne change
            // pas : c'est l'endroit où le fichier a été catalogué, et une lettre de lecteur de passage n'a pas
            // à le réécrire.
            Journal($"Fichier lu depuis un autre lecteur : {located.File.Path} → {located.Path}");
            Status = Tr.F("Fichier trouvé sur un autre lecteur : {0}", located.Path);
        }

        return located;
    }

    /// <summary>Racines où chercher un fichier absent ; nul pour celles du système. Les tests y mettent les leurs.</summary>
    public Func<IReadOnlyList<string>>? MediaRoots { get; set; }

    /// <param name="ask">Qui pose la question « branchez le disque » : la fenêtre principale par défaut, la fiche quand on lit depuis elle.</param>
    private async Task PlayTitleAsync(long titleId, IReadOnlyList<long>? keepSelection, MediaLocator.Ask? ask = null)
    {
        try
        {
            await PlayLocatedAsync(titleId, keepSelection, ask);
        }
        finally
        {
            // Lire a pu brancher un disque, ou le trouver absent : la pastille dit ce qu'il en est maintenant.
            if (Detail?.Id == titleId)
            {
                _ = Record.CheckPresenceAsync();
            }
        }
    }

    private async Task PlayLocatedAsync(long titleId, IReadOnlyList<long>? keepSelection, MediaLocator.Ask? ask)
    {
        if (_media is null || _media.Files.FilesOf(titleId).Count == 0)
        {
            Status = MissingMediaMessage();
            return;
        }

        if (await LocateFileAsync(titleId, ask) is not { } located)
        {
            Status = MissingMediaMessage();
            return;
        }

        try
        {
            Player.Play(located.Path, _media.Settings.Get(MediaContext.PlayerKey), _media.Settings.Get(MediaContext.PlayerArgsKey));
            Journal($"Lecture : {located.Path}");
            if (_media.Settings.Get(MediaContext.MarkSeenOnPlayKey) != "false")
            {
                // Chaque lecture laisse sa trace datée : c'est ce qui fait l'historique, et « revu trois fois ».
                _titles.AddViewing(titleId, DateTimeOffset.Now, ViewingSource.Playback);
                Touch([titleId], keepSelection);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Status = ex.Message;
        }
    }

    /// <summary>
    /// Actions au survol d'une ligne ou d'une affiche : elles visent le titre survolé et laissent la
    /// sélection telle qu'elle est. Marquer vu un film n'oblige plus à le sélectionner d'abord.
    /// </summary>
    [RelayCommand]
    private Task PlayOfAsync(TitleSummary? title)
    {
        if (title is null || _media is null)
        {
            Status = MissingMediaMessage();
            return Task.CompletedTask;
        }

        return PlayTitleAsync(title.Id, SelectedIds);
    }

    [RelayCommand]
    private void ToggleSeenOf(TitleSummary? title)
    {
        if (title is null)
        {
            return;
        }

        _titles.SetSeen([title.Id], !title.IsSeen, DateTimeOffset.Now);
        Journal($"Marqué {(title.IsSeen ? "non vu" : "vu")} : {title.Title}");
        RefreshTitles([title.Id], SelectedIds);
    }

    [RelayCommand]
    private void ToggleFavoriteOf(TitleSummary? title)
    {
        if (title is null)
        {
            return;
        }

        _titles.SetFavorite([title.Id], !title.IsFavorite);
        Journal($"{(title.IsFavorite ? "Retiré des" : "Ajouté aux")} favoris : {title.Title}");
        RefreshTitles([title.Id], SelectedIds);
    }

    [RelayCommand(CanExecute = nameof(HasFiles))]
    private async Task OpenFolderAsync()
    {
        if (Detail is not { } detail || await LocateFileAsync(detail.Id) is not { } located)
        {
            Status = MissingMediaMessage();
            return;
        }

        try
        {
            Player.RevealInFolder(located.Path);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Status = ex.Message;
        }
    }

    private bool HasFiles() => DetailFiles.Count > 0;

    /// <summary>
    /// Ouvre la bande-annonce dans le navigateur. TMDb la fournit (YouTube) et un fichier .nfo peut en porter
    /// une autre ; seules les plateformes vidéo connues sont ouvertes.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasTrailer))]
    private void PlayTrailer()
    {
        if (!WebLinks.OpenVideo(Detail?.TrailerUrl))
        {
            Status = Tr.T("Bande-annonce : l'adresse enregistrée n'est pas une plateforme vidéo connue.");
        }
    }

    public bool HasTrailer() => !string.IsNullOrWhiteSpace(Detail?.TrailerUrl);

    /// <summary>Saisons et épisodes de la série sélectionnée.</summary>
    [RelayCommand(CanExecute = nameof(IsSeriesSelected))]
    private async Task OpenSeriesAsync()
    {
        if (Detail is not { } detail || _media?.Series is not { } series)
        {
            return;
        }

        var vm = new SeriesViewModel(detail.Id, detail.Title, detail.TmdbId, series, _metadata, _media.Files, _media.Settings) { MediaRoots = MediaRoots };
        if (await _dialogs.ShowSeriesAsync(vm))
        {
            Touch([detail.Id]);
        }
    }

    public bool IsSeriesSelected() => Detail?.Kind == TitleKind.Series && _media?.Series is not null;

    /// <summary>Séries avec des épisodes diffusés non vus (lien « Suivi des séries » du bandeau gauche).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SeriesTrackingLabel))]
    public partial int SeriesToWatch { get; private set; }

    [ObservableProperty]
    public partial bool HasSeries { get; private set; }

    public string SeriesTrackingLabel => SeriesToWatch > 0 ? Tr.F("Suivi des séries ({0})", SeriesToWatch) : Tr.T("Suivi des séries");

    private void UpdateSeriesTracking()
    {
        if (_media?.Series is not { } series)
        {
            return;
        }

        var (count, toWatch) = series.CountToWatch(DateOnly.FromDateTime(DateTime.Now));
        HasSeries = count > 0;
        SeriesToWatch = toWatch;
    }

    /// <summary>Suivi des séries : épisodes à regarder, prochaines diffusions.</summary>
    [RelayCommand]
    private async Task OpenSeriesTrackingAsync()
    {
        if (_media?.Series is not { } series)
        {
            return;
        }

        var tracking = new SeriesTrackingViewModel(series, _metadata, relative => relative is null ? null : _images?.ResolveInside(relative));
        var chosen = await _dialogs.ShowSeriesTrackingAsync(tracking) && tracking.ChosenTitleId is not null;
        UpdateSeriesTracking();
        if (chosen)
        {
            GoToTitle(tracking.ChosenTitleId!.Value);
        }
        else if (tracking.Changed)
        {
            Refresh(selectIds: SelectedIds);
        }
    }

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

    /// <summary>Copie les chemins des fichiers de la sélection : de quoi les reprendre dans un script ou un transfert.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task CopyPathsAsync()
    {
        if (_media is not { } media)
        {
            return;
        }

        var paths = SelectedIds.SelectMany(id => media.Files.FilesOf(id)).Select(f => f.FullPath).ToList();
        if (paths.Count == 0)
        {
            Status = Tr.T("Aucun fichier vidéo dans la sélection.");
            return;
        }

        Status = await _dialogs.CopyTextAsync(string.Join(Environment.NewLine, paths))
            ? Tr.F("{0} chemin(s) copié(s)", paths.Count)
            : Tr.T("Presse-papiers indisponible.");
    }

    /// <summary>Marque la sélection comme possédée, ou la renvoie en liste de souhaits.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void SetOwned(bool owned)
    {
        var ids = SelectedIds;
        _titles.SetOwned(ids, owned);
        Journal($"Marqué {(owned ? "possédé" : "liste de souhaits")} : {Names(SelectedTitles.ToList())}");
        Status = owned
            ? Tr.F("{0} titre(s) marqué(s) possédé(s)", ids.Count)
            : Tr.F("{0} titre(s) remis en liste de souhaits", ids.Count);
        RefreshTitles(ids);
    }

    /// <summary>Ajoute la sélection à une liste personnelle existante, à la fin de celle-ci.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void AddToList(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var ids = SelectedIds;
        _titles.ApplyBulk(ids, new BulkEdit { AddGroups = [name] });
        _lastList = name;
        ReloadSideLists();
        Status = Tr.F("{0} titre(s) ajouté(s) à « {1} »", ids.Count, name);
        Journal($"Ajouté à la liste « {name} » : {Names(SelectedTitles.ToList())}");
    }

    private string? _lastList;

    /// <summary>
    /// Raccourci clavier d'« Ajouter à une liste » : la dernière liste utilisée, ou la seule qui existe. Un
    /// sous-menu ne se déclenche pas d'une touche ; ranger une série de films dans la même liste, si.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void AddToLastList()
    {
        var target = _lastList is { } last && SideBar.ListNames.Contains(last) ? last : SideBar.ListNames.Count == 1 ? SideBar.ListNames[0] : null;
        if (target is null)
        {
            Status = SideBar.ListNames.Count == 0
                ? Tr.T("Aucune liste personnelle : créez-en une depuis la fiche d'un film ou la modification en lot.")
                : Tr.T("Choisissez d'abord une liste au clic droit : le raccourci reprendra ensuite la même.");
            return;
        }

        AddToList(target);
    }

    /// <summary>Affiche les films d'une personne : la recherche remplace la vue en cours.</summary>
    [RelayCommand]
    private void ShowPeopleOf(FacetItem? person)
    {
        if (person is null)
        {
            return;
        }

        Home = null;
        PeopleBrowser = null;
        SideBar.ReleaseAll();
        ReleaseDuplicateGroup();
        Rules = person.Search;
    }

    private bool CanRenameFiles() => _extras.Renames is not null && SelectedTitles.Count > 0 && !IsBusy;

    /// <summary>
    /// Renomme les fichiers vidéo de la sélection d'après leur fiche. Le plan est montré avant d'être
    /// appliqué : c'est la seule action du menu qui touche au disque, et elle ne se défait pas.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRenameFiles))]
    private async Task RenameFilesAsync()
    {
        if (_extras.Renames is not { } renames)
        {
            return;
        }

        var planned = renames.Plan(SelectedIds);
        var doable = planned.Where(p => p.CanRename).ToList();
        if (doable.Count == 0)
        {
            Status = planned.Count == 0
                ? Tr.T("Aucun fichier vidéo dans la sélection.")
                : Tr.F("Rien à renommer : {0}", string.Join(", ", planned.Select(p => p.Problem).Distinct()));
            return;
        }

        var preview = string.Join("\n", doable.Take(8).Select(p => $"· {p.OldName}  →  {p.NewName}"));
        var skipped = planned.Count - doable.Count;
        if (!await _dialogs.ConfirmAsync(
                "Renommer les fichiers",
                Tr.F("Renommer {0} fichier(s) sur le disque d'après leur fiche ?\n\n{1}{2}\n\nLe .nfo qui accompagne un fichier est renommé avec lui. Cette action ne se défait pas.",
                    doable.Count,
                    preview + (doable.Count > 8 ? "\n· …" : string.Empty),
                    skipped == 0 ? string.Empty : Tr.F("\n\n{0} fichier(s) laissé(s) de côté (déjà au bon nom, introuvable ou nom déjà pris).", skipped))))
        {
            return;
        }

        var done = renames.Apply(doable, out var problems);
        Touch(SelectedIds);
        Status = problems.Count == 0
            ? Tr.F("{0} fichier(s) renommé(s)", done)
            : Tr.F("{0} fichier(s) renommé(s), {1} échec(s)", done, problems.Count);
        Journal($"Renommage de {done} fichier(s) d'après leur fiche, {problems.Count} échec(s)");
        if (problems.Count > 0)
        {
            await _dialogs.InformAsync("Renommer les fichiers", string.Join('\n', problems.Take(20)));
        }
    }

    private bool CanMergeSelection() => Merger is not null && SelectedTitles.Count > 1 && !IsBusy;

    /// <summary>
    /// Fusionne les titres sélectionnés en une seule fiche, sur la seule affirmation de l'utilisateur : rien
    /// ne vérifie qu'ils désignent le même film. D'où l'avertissement, et la sauvegarde faite avant.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanMergeSelection))]
    private async Task MergeSelectionAsync()
    {
        if (Merger is not { } merger)
        {
            return;
        }

        var chosen = SelectedTitles.ToList();
        var keep = chosen.OrderByDescending(t => t.Id == chosen.Min(c => c.Id) ? 0 : 1).First();
        if (!await _dialogs.ConfirmAsync(
                "Fusionner les films sélectionnés",
                Tr.F("Réunir ces {0} fiches en une seule ?\n\n{1}\n\nAttention : Cinedex ne vérifie pas qu'il s'agit du même film, c'est vous qui l'affirmez. Les fiches absorbées sont supprimées et leurs fichiers vidéo rattachés à celle qui reste ; rien n'est effacé sur le disque, et une sauvegarde de la base est faite avant.",
                    chosen.Count, string.Join("\n", chosen.Take(8).Select(t => "· " + t.TitleWithYear)) + (chosen.Count > 8 ? "\n· …" : string.Empty))))
        {
            return;
        }

        TitleMerge result;
        try
        {
            // La sauvegarde annoncée dans la question est faite ici, par le chemin commun à toutes les fusions.
            result = merger.MergeChosen(chosen.Select(t => t.Id).ToList());
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException or UnauthorizedAccessException)
        {
            Status = ex.Message;
            return;
        }

        ReloadSideLists();
        Refresh(selectId: keep.Id);
        Status = result.Merged == 0 ? Tr.T("Rien à fusionner.") : DuplicateMerger.Describe(result);
    }

    /// <summary>
    /// Réécrit le .nfo des fichiers de la sélection en remplaçant les valeurs déjà présentes, là où
    /// « Écrire les .nfo » se contente de compléter les champs vides. C'est ce qu'il faut après avoir corrigé
    /// des fiches : sans cela, le .nfo garde ce qu'il avait de faux. Une copie .nfo.bak est faite si le
    /// réglage le demande, et aucun fichier vidéo n'est touché.
    /// </summary>
    [RelayCommand]
    private async Task UpdateNfoAsync()
    {
        if (_media?.Nfo is not { } nfo)
        {
            return;
        }

        var ids = SelectedTitles.Count > 0 ? SelectedIds : Titles.Select(t => t.Id).ToList();
        var scope = SelectedTitles.Count > 0
            ? Tr.F("les {0} titre(s) sélectionné(s)", ids.Count)
            : Tr.F("les {0} titre(s) affiché(s)", ids.Count);
        if (ids.Count == 0 || !await _dialogs.ConfirmAsync(
                "Mettre à jour les .nfo",
                Tr.F("Réécrire les .nfo existants de {0} depuis les fiches de Cinedex ?\nLes valeurs déjà présentes sont remplacées. Aucun .nfo n'est créé, et les fichiers vidéo ne sont pas touchés.", scope)))
        {
            return;
        }

        // « Mettre à jour » ne crée rien de lui-même : un film sans .nfo reste sans, sauf demande expresse.
        var options = new Cinedex.Media.NfoWriteOptions { OverwriteExisting = true, OnlyExisting = true, Backup = nfo.Settings.Backup };
        var written = ids.Sum(id => nfo.WriteForTitle(id, options));
        var created = 0;

        // Les films sans .nfo ne sont pas passés sous silence : on demande, une fois, s'il faut les créer.
        if (nfo.WithoutNfo(ids) is { Count: > 0 } missing && await _dialogs.ConfirmAsync(
                "Créer les .nfo manquants",
                Tr.F("{0} film(s) de la sélection n'ont pas de .nfo. Les créer maintenant ?", missing.Count)))
        {
            created = missing.Sum(id => nfo.WriteForTitle(id, options with { OnlyExisting = false }));
        }

        Status = created == 0
            ? Tr.F("{0} fichier(s) .nfo mis à jour", written)
            : Tr.F("{0} fichier(s) .nfo mis à jour, {1} créé(s)", written, created);
        Journal($"{written} fichier(s) .nfo mis à jour depuis les fiches, {created} créé(s)");
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
            var (read, tool) = await _media.Import.RefreshTechnicalAsync(ids, TrackedProgress(ids.Sum(id => _media.Files.FilesOf(id).Count)), CancellationToken.None);
            Touch(SelectedIds);
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

    /// <summary>Modification groupée de la sélection ; « Modifier la fiche » y mène aussi quand il y en a plusieurs.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task BulkEditAsync()
    {
        var ids = SelectedIds;
        var edit = new BulkEditViewModel(ids.Count, _titles.RefItems("medium"), _titles.Collections());
        if (await _dialogs.BulkEditAsync(edit))
        {
            _titles.ApplyBulk(ids.ToList(), edit.Build());
            Touch(ids);
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
    private void Reload()
    {
        ReloadSideLists();
        Refresh(selectIds: SelectedIds);
    }

    private bool HasSelection() => SelectedTitles.Count > 0;

    private bool HasSingleSelection() => SelectedTitles.Count == 1;

    private void Refresh(long? selectId = null, IReadOnlyList<long>? selectIds = null)
    {
        // Les propriétés initialisées dans le constructeur déclenchent Refresh avant que tout soit prêt.
        if (!_loaded)
        {
            return;
        }

        // Ce qui a été demandé en arrière-plan avant cet instant est dépassé : la liste lue ici fait foi.
        ++_searchGeneration;
        ReviewCount = _metadata?.Reviews?.Count() ?? 0;
        UpdateSeriesTracking();
        Apply(_titles.Query(CurrentQuery).Select(Prepare).ToList(), selectIds ?? (selectId is { } one ? [one] : []));
    }

    private IReadOnlyList<long>? _viewSelect;

    private int _viewGeneration;

    private bool _viewRefreshScheduled;

    /// <summary>
    /// Dernière relecture de vue demandée ; terminée quand la liste affichée correspond au filtre, au tri et
    /// au bandeau courants. Les tests d'interface l'attendent, l'application n'en a pas besoin.
    /// </summary>
    public Task ViewRefresh { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Relit la liste après un changement de vue : filtre, tri, entrée du bandeau, recherche effacée. La
    /// requête part en arrière-plan, parce qu'elle coûte 150 à 700 ms sur 12 000 titres et que la fenêtre
    /// gelait d'autant. Plusieurs changements dans la même foulée (un filtre qui en relâche un autre) ne
    /// donnent qu'une requête, et un résultat dépassé par une demande plus récente n'est jamais appliqué.
    /// </summary>
    private void RefreshView(IReadOnlyList<long>? selectIds = null)
    {
        if (!_loaded)
        {
            return;
        }

        if (Avalonia.Application.Current is null)
        {
            // Sans application (tests du modèle seul), rien ne viendrait exécuter la suite : on lit sur place.
            Refresh(selectIds: selectIds);
            return;
        }

        _viewSelect = selectIds;
        _viewGeneration = ++_searchGeneration;
        if (_viewRefreshScheduled)
        {
            return;
        }

        _viewRefreshScheduled = true;
        var done = new TaskCompletionSource();
        ViewRefresh = done.Task;
        Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
        {
            _viewRefreshScheduled = false;
            try
            {
                // Une relecture synchrone passée entre-temps a déjà mis la liste à jour : ne pas la défaire.
                if (_viewGeneration == _searchGeneration)
                {
                    await RefreshAsync(_viewSelect ?? []);
                }
            }
            finally
            {
                done.TrySetResult();
            }
        });
    }

    /// <summary>Installe les lignes trouvées : sélection conservée, compte rendu, état de la liste vide.</summary>
    private void Apply(IReadOnlyList<TitleSummary> rows, IReadOnlyList<long> keep)
    {
        _refreshing = true;
        Titles.ReplaceAll(rows);

        SelectedTitles.Clear();
        if (keep.Count > 0)
        {
            // Un ensemble, pas une liste : garder 6 000 titres parmi 12 000 ne doit pas coûter 72 millions de comparaisons.
            var kept = keep as IReadOnlySet<long> ?? keep.ToHashSet();
            foreach (var title in Titles.Where(t => kept.Contains(t.Id)))
            {
                SelectedTitles.Add(title);
            }
        }

        _refreshing = false;
        PublishSelection();
        Status = SelectedFilter.Value == QuickFilter.All && string.IsNullOrWhiteSpace(SearchText)
            ? Tr.F("{0} titre(s) dans le catalogue", Titles.Count)
            : Tr.F("{0} titre(s) trouvé(s)", Titles.Count);
        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>
    /// Recherche de la liste principale, cherchée hors du fil de l'interface : la frappe n'attend jamais la base,
    /// même sur un gros catalogue. Une frappe plus récente annule le résultat d'une recherche dépassée.
    /// </summary>
    private int _searchGeneration;

    private Task SearchAsync() => RefreshAsync(select: null);

    /// <summary>
    /// Lit la liste en arrière-plan et l'installe si elle est toujours la plus récente demandée.
    /// <paramref name="select"/> donne la sélection à poser ; sans lui, celle du moment est gardée.
    /// </summary>
    private async Task RefreshAsync(IReadOnlyList<long>? select)
    {
        if (!_loaded)
        {
            return;
        }

        var generation = ++_searchGeneration;
        var query = CurrentQuery;
        List<TitleSummary> rows;
        int reviews;
        try
        {
            (rows, reviews) = await Task.Run(() =>
                (_titles.Query(query).Select(Prepare).ToList(), _metadata?.Reviews?.Count() ?? 0));
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex)
        {
            Status = ex.Message;
            return;
        }

        if (generation == _searchGeneration)
        {
            ReviewCount = reviews;
            UpdateSeriesTracking();
            Apply(rows, select ?? SelectedIds);
        }
    }

    internal static (string Title, int? Year) ParseTitleAndYear(string input) => TitleText.SplitYear(input);

    /// <summary>Ligne prête à afficher : chemin absolu de l'affiche, ligne secondaire selon les colonnes choisies.</summary>
    private TitleSummary Prepare(TitleSummary title) => title with
    {
        PosterPath = title.PosterRelativePath is { } poster && _images is not null ? _images.PathInside(poster) : null,
        CustomSubtitle = Columns.Subtitle(title),
    };

    /// <summary>Au-delà de ce nombre de titres touchés, relire toute la liste coûte moins que les remplacer un par un.</summary>
    private const int TargetedRefreshLimit = 200;

    /// <summary>
    /// Relit ce qu'une action a modifié : les seules lignes touchées quand elles sont peu nombreuses (une
    /// demi-milliseconde au lieu d'une relecture de tout le catalogue), la liste entière sinon.
    /// </summary>
    private void Touch(IReadOnlyList<long> ids, IReadOnlyList<long>? selectIds = null)
    {
        if (ids.Count is > 0 and <= TargetedRefreshLimit)
        {
            RefreshTitles(ids, selectIds);
        }
        else
        {
            Refresh(selectIds: selectIds ?? ids);
        }
    }

    /// <summary>
    /// Ce qui place une ligne dans le tri courant. Deux valeurs égales : la ligne n'a pas bougé de rang. Le
    /// tri manuel d'une liste personnelle ne change que par les commandes de déplacement, qui relisent tout.
    /// </summary>
    private object? SortKey(TitleSummary title) => SelectedSort.Value switch
    {
        SortField.Title => (title.Title, title.Year),
        SortField.Year => (title.Year, title.Title),
        SortField.Number => (title.Number, title.Title),
        SortField.AddedAt => (title.AddedOn, title.Title),
        SortField.UserRating => (title.UserRating, title.Title),
        SortField.Runtime => (title.RuntimeMinutes, title.Title),
        _ => null,
    };

    /// <summary>
    /// Met à jour quelques titres de la liste sans tout relire (favori, vu, prêt) : chaque ligne est remplacée en
    /// place, ou retirée si elle ne correspond plus au filtre. Si un titre doit apparaître dans la liste, toute la
    /// liste est relue pour le placer au bon rang.
    /// </summary>
    private void RefreshTitles(IReadOnlyList<long> ids, IReadOnlyList<long>? selectIds = null)
    {
        if (!_loaded)
        {
            return;
        }

        var wanted = ids.ToHashSet();
        var current = Titles.Select((t, i) => (t.Id, i)).Where(x => wanted.Contains(x.Id)).ToDictionary(x => x.Id, x => x.i);
        var fresh = _titles.Query(CurrentQuery with { Ids = ids }).ToDictionary(t => t.Id);

        // Un titre qui doit apparaître, ou dont la valeur de tri a changé (un titre renommé, une année
        // corrigée), n'est plus à sa place : seule la relecture complète le range au bon rang.
        if (fresh.Keys.Any(id => !current.ContainsKey(id)) ||
            fresh.Values.Any(title => !Equals(SortKey(title), SortKey(Titles[current[title.Id]]))))
        {
            Refresh(selectIds: selectIds ?? ids);
            return;
        }

        _refreshing = true;
        foreach (var (id, index) in current.OrderByDescending(x => x.Value))
        {
            if (fresh.TryGetValue(id, out var title))
            {
                Titles[index] = Prepare(title);
            }
            else
            {
                Titles.RemoveAt(index);
            }
        }

        var keep = (selectIds ?? ids).ToHashSet();
        SelectedTitles.Clear();
        foreach (var title in Titles.Where(t => keep.Contains(t.Id)))
        {
            SelectedTitles.Add(title);
        }

        _refreshing = false;
        PublishSelection();
        OnPropertyChanged(nameof(IsEmpty));
    }
}
