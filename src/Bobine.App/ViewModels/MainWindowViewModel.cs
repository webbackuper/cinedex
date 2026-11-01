using System.Collections.ObjectModel;
using System.Globalization;
using Bobine.App.Services;
using Bobine.Core;
using Bobine.Core.Metadata;
using Bobine.Core.People;
using Bobine.Core.Titles;
using Bobine.Data;
using Bobine.Media;
using Bobine.Metadata.Tmdb;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bobine.App.ViewModels;

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

    public IReadOnlyList<FilterItem> Filters { get; } =
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
        new(QuickFilter.StandardDefinition, "Définition standard"),
        new(QuickFilter.HighDefinition, "Haute définition"),
        new(QuickFilter.UltraHighDefinition, "Ultra HD (4K)"),
        new(QuickFilter.ThreeD, "3D"),
        new(QuickFilter.PhysicalMedia, "Supports physiques"),
        new(QuickFilter.MediaFiles, "Fichiers vidéo"),
        new(QuickFilter.MissingFiles, "Fichiers introuvables"),
    ];

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
            Criteria = null;
            _loaded = true;
        }

        Refresh(selectId: titleId);
    }

    public string DetailFilesText => string.Join('\n', DetailFiles.Select(f => (f.IsMissing ? "introuvable : " : string.Empty) + f.FullPath));

    partial void OnDetailFilesChanged(IReadOnlyList<MediaFileEntry> value) => OnPropertyChanged(nameof(DetailFilesText));

    public bool HasMedia => _media is not null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddFromTmdbCommand), nameof(LinkToTmdbCommand), nameof(UpdateAllFromTmdbCommand))]
    public partial bool IsBusy { get; set; }

    public bool HasMetadataSource => _metadata is not null;

    public bool HasDetail => Detail is not null;

    public bool IsSeries => Detail?.Kind == TitleKind.Series;

    public bool IsEmpty => _loaded && Titles.Count == 0;

    public string DataLocation => _paths.IsPortable ? $"Mode portable : {_paths.DataRoot}" : _paths.DataRoot;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCriteria))]
    public partial SearchCriteria? Criteria { get; set; }

    public bool HasCriteria => Criteria is { IsEmpty: false };

    partial void OnCriteriaChanged(SearchCriteria? value) => Refresh();

    private TitleQuery CurrentQuery => new(SearchText, SelectedFilter.Value, SelectedSort.Value, SortDescending, Criteria: Criteria);

    private IReadOnlyList<long> SelectedIds => SelectedTitles.Select(t => t.Id).ToList();

    public void Load()
    {
        _loaded = true;
        IsGridView = _media?.Settings.Get(GridViewKey) == "true";
        Refresh();
    }

    partial void OnSearchTextChanged(string value) => Refresh();

    partial void OnSelectedFilterChanged(FilterItem value) => Refresh();

    partial void OnSelectedSortChanged(Choice<SortField> value) => Refresh();

    partial void OnSortDescendingChanged(bool value) => Refresh();

    private void OnSelectionChanged()
    {
        Detail = SelectedTitles.Count == 1 ? _titles.Get(SelectedTitles[0].Id) : null;
        PosterPath = Detail is null ? null : _images?.PathFor(Detail.Id, "poster");
        BackdropPath = Detail is null ? null : _images?.PathFor(Detail.Id, "backdrop");
        DetailFiles = Detail is null || _media is null ? [] : _media.Files.FilesOf(Detail.Id);
        TechnicalSummary = Detail is null ? null : _media?.Files.TechnicalSummary(Detail.Id);
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

        var editor = new TitleEditorViewModel(
            current, _titles.Genres(), _titles.RefItems("medium"), _titles.RefItems("case"), _titles.RefItems("version"), _titles.Collections());
        // La fenêtre d'édition valide elle-même (TryBuild) avant de se fermer sur « Enregistrer ».
        if (await _dialogs.EditTitleAsync(editor) && editor.TryBuild(out var updated))
        {
            _titles.Update(updated);
            Refresh(selectId: updated.Id);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task DeleteAsync()
    {
        var ids = SelectedIds;
        var question = ids.Count == 1
            ? $"Supprimer « {SelectedTitles[0].Title} » du catalogue ?"
            : $"Supprimer {ids.Count} titres du catalogue ?";
        if (await _dialogs.ConfirmAsync("Supprimer", question + "\nLes fichiers vidéo sur le disque ne sont pas touchés."))
        {
            var count = _titles.Delete(ids);
            Refresh();
            Status = $"{count} titre(s) supprimé(s)";
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void ToggleSeen()
    {
        var ids = SelectedIds;
        var markSeen = SelectedTitles.Any(t => !t.IsSeen);
        _titles.SetSeen(ids, markSeen, DateTimeOffset.Now);
        Refresh(selectIds: ids);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void ToggleFavorite()
    {
        var ids = SelectedIds;
        _titles.SetFavorite(ids, SelectedTitles.Any(t => !t.IsFavorite));
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
        Status = $"Au hasard : {pick.Title}";
    }

    [RelayCommand(CanExecute = nameof(CanLend))]
    private void Lend()
    {
        try
        {
            _titles.Lend(Detail!.Id, BorrowerText, DateTimeOffset.Now);
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
            : string.Join('\n', history.Select(l =>
                $"{l.Borrower} : du {l.LoanedAt.ToLocalTime():d} " +
                (l.ReturnedAt is { } r ? $"au {r.ToLocalTime():d} ({(r - l.LoanedAt).Days} j)" : "à aujourd'hui, pas encore rendu")));
        await _dialogs.InformAsync($"Historique des prêts : {Detail.Title}", text);
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
            Status = created ? $"« {choice.Title} » ajouté depuis TMDb" : $"« {choice.Title} » est déjà dans le catalogue";
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
        var scope = SelectedTitles.Count > 0 ? $"les {ids.Count} titre(s) sélectionné(s)" : $"les {ids.Count} titre(s) affiché(s)";
        if (ids.Count == 0 || !await _dialogs.ConfirmAsync(
                "Mettre à jour via TMDb",
                $"Mettre à jour {scope} depuis TMDb ?\nLes fiches existantes sont complétées sur place, aucune entrée n'est créée. Les titres verrouillés sont ignorés."))
        {
            return;
        }

        await RunMetadataAsync(async () =>
        {
            var targets = ids.Select(_titles.Get).OfType<TitleDetail>().ToList();
            var progress = new Progress<string>(message => Status = message);
            var result = await _metadata!.UpdateTitlesAsync(targets, ImportOptions.Default, progress, CancellationToken.None);
            Refresh(selectIds: ids);
            Status = $"{result.Updated} mis à jour, {result.NotFound} non trouvé(s), {result.Duplicates} doublon(s) évité(s), {result.Skipped} ignoré(s), {result.Failed} échec(s)";
            if (result.Messages is { Count: > 0 } messages)
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
            await _dialogs.EditSettingsAsync(_settings());
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
        if (_media is not null && await _dialogs.ImportFolderAsync(new ImportFolderViewModel(
                    _media.Import,
                    _media.Settings.Get(MediaContext.IgnoreBracketedKey) != "false",
                    MediaContext.NoiseWords(_media.Settings))))
        {
            Refresh(selectIds: SelectedIds);
        }
    }

    /// <summary>Lit le premier fichier présent ; marque le titre comme vu si le réglage le demande (par défaut).</summary>
    [RelayCommand(CanExecute = nameof(HasFiles))]
    private void Play()
    {
        var file = DetailFiles.FirstOrDefault(f => File.Exists(f.FullPath));
        if (file is null)
        {
            Status = "Fichier introuvable : le disque est peut-être débranché. « Vérifier les fichiers » peut le retrouver.";
            return;
        }

        try
        {
            Player.Play(file.FullPath, _media!.Settings.Get(MediaContext.PlayerKey), _media.Settings.Get(MediaContext.PlayerArgsKey));
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
            Status = "Fichier introuvable.";
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
            Status = $"Notes IMDb actualisées pour {count} titre(s)";
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException)
        {
            Status = $"Fichier IMDb indisponible : {ex.Message}";
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
        Status = $"{written} fichier(s) .nfo créé(s) ou complété(s)";
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
                : $"{read} fichier(s) analysé(s) avec {tool}";
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
            Status = $"{missing} fichier(s) introuvable(s), {relinked} retrouvé(s) après déplacement";
            if (missing > 0)
            {
                SelectedFilter = Filters.Single(f => f.Value == QuickFilter.MissingFiles);
            }
            else
            {
                Refresh(selectIds: SelectedIds);
            }
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
            Status = $"{ids.Count} titres modifiés";
        }
    }

    [RelayCommand]
    private async Task AdvancedSearchAsync()
    {
        var search = new AdvancedSearchViewModel(Criteria, _titles.Genres(), _titles.Tags(), _titles.Groups(), _titles.Collections());
        if (await _dialogs.AdvancedSearchAsync(search))
        {
            Criteria = search.Build();
        }
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
        SelectedFilter = Filters[0];
        Criteria = null;
    }

    public string CatalogName => _catalogs?.Current ?? AppPaths.DefaultCatalogName;

    public string CatalogLabel => $"Catalogue : {_catalogs?.Current ?? AppPaths.DefaultCatalogName}";

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
        var label = SelectedFilter.Value == QuickFilter.All && string.IsNullOrWhiteSpace(SearchText)
            ? "dans le catalogue"
            : "trouvé(s)";
        Status = string.Create(CultureInfo.CurrentCulture, $"{Titles.Count} titre(s) {label}");
        OnPropertyChanged(nameof(IsEmpty));
    }

    internal static (string Title, int? Year) ParseTitleAndYear(string input) => TitleText.SplitYear(input);
}
