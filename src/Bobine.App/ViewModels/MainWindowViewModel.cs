using System.Collections.ObjectModel;
using System.Globalization;
using Bobine.App.Services;
using Bobine.Core;
using Bobine.Core.Metadata;
using Bobine.Core.Titles;
using Bobine.Data;
using Bobine.Metadata.Tmdb;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bobine.App.ViewModels;

public sealed record Choice<T>(T Value, string Label)
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
    private bool _refreshing;
    private bool _loaded;

    public MainWindowViewModel(
        TitleRepository titles,
        AppPaths paths,
        IDialogs dialogs,
        MetadataService? metadata = null,
        ImageStore? images = null,
        Func<SettingsViewModel>? settings = null)
    {
        _titles = titles;
        _paths = paths;
        _dialogs = dialogs;
        _metadata = metadata;
        _images = images;
        _settings = settings;
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

    public IReadOnlyList<Choice<QuickFilter>> Filters { get; } =
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
    public partial Choice<QuickFilter> SelectedFilter { get; set; }

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
    [NotifyCanExecuteChangedFor(nameof(AddFromTmdbCommand), nameof(LinkToTmdbCommand), nameof(UpdateAllFromTmdbCommand))]
    public partial bool IsBusy { get; set; }

    public bool HasMetadataSource => _metadata is not null;

    public bool HasDetail => Detail is not null;

    public string DataLocation => _paths.IsPortable ? $"Mode portable : {_paths.DataRoot}" : _paths.DataRoot;

    private TitleQuery CurrentQuery => new(SearchText, SelectedFilter.Value, SelectedSort.Value, SortDescending);

    private IReadOnlyList<long> SelectedIds => SelectedTitles.Select(t => t.Id).ToList();

    public void Load()
    {
        _loaded = true;
        Refresh();
    }

    partial void OnSearchTextChanged(string value) => Refresh();

    partial void OnSelectedFilterChanged(Choice<QuickFilter> value) => Refresh();

    partial void OnSelectedSortChanged(Choice<SortField> value) => Refresh();

    partial void OnSortDescendingChanged(bool value) => Refresh();

    private void OnSelectionChanged()
    {
        Detail = SelectedTitles.Count == 1 ? _titles.Get(SelectedTitles[0].Id) : null;
        PosterPath = Detail is null ? null : _images?.PathFor(Detail.Id, "poster");
        LinkToTmdbCommand.NotifyCanExecuteChanged();
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

    [RelayCommand(CanExecute = nameof(HasSingleSelection))]
    private async Task EditAsync()
    {
        var current = _titles.Get(SelectedTitles[0].Id);
        if (current is null)
        {
            return;
        }

        var editor = new TitleEditorViewModel(current, _titles.Genres(), _titles.RefItems("medium"), _titles.RefItems("case"), _titles.RefItems("version"));
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

    [RelayCommand(CanExecute = nameof(CanUseMetadata))]
    private async Task UpdateAllFromTmdbAsync()
    {
        if (!await _dialogs.ConfirmAsync("Mise à jour par lot", "Mettre à jour depuis TMDb tous les titres déjà liés ? Les titres verrouillés sont ignorés."))
        {
            return;
        }

        await RunMetadataAsync(async () =>
        {
            var progress = new Progress<string>(message => Status = message);
            var result = await _metadata!.UpdateAllAsync(ImportOptions.Default with { DownloadBackdrop = false }, progress, CancellationToken.None);
            Refresh(selectIds: SelectedIds);
            Status = $"{result.Updated} mis à jour, {result.Skipped} ignoré(s), {result.Failed} échec(s)";
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
        try
        {
            await action();
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
            Titles.Add(title);
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
    }

    internal static (string Title, int? Year) ParseTitleAndYear(string input) => TitleText.SplitYear(input);
}
