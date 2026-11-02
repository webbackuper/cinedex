using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media.Imaging;
using Cinedex.App.Services;
using Cinedex.Core.Localization;
using Cinedex.Core.Metadata;
using Cinedex.Data;
using Cinedex.Metadata.Tmdb;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cinedex.App.ViewModels;

/// <summary>Une collection dans la liste : affiche, nom et films possédés sur le nombre de films de la saga.</summary>
public sealed class CollectionRow(CollectionSummary summary, string? posterPath)
{
    public CollectionSummary Summary { get; } = summary;

    public long Id => Summary.Id;

    public string Name => Summary.Name;

    public string? PosterPath { get; } = posterPath;

    public bool IsIncomplete => Summary.Parts > Summary.Owned;

    public string CountLabel => Summary.Parts > 0
        ? Tr.F("{0} / {1} films", Summary.Owned, Summary.Parts)
        : Tr.F("{0} film(s)", Summary.Owned);
}

/// <summary>Un film de la saga : présent au catalogue (cliquable) ou manquant (lien TMDb).</summary>
public sealed partial class CollectionFilm : ObservableObject
{
    public CollectionFilm(long? titleId, long? sourceId, string title, int? year, string? posterPath, string? remotePoster)
    {
        TitleId = titleId;
        SourceId = sourceId;
        Title = title;
        Year = year;
        PosterPath = posterPath;
        RemotePoster = remotePoster;
    }

    public long? TitleId { get; }

    public long? SourceId { get; }

    public string Title { get; }

    public int? Year { get; }

    public string? PosterPath { get; }

    /// <summary>Affiche TMDb d'un film manquant, chargée à l'affichage de la collection.</summary>
    public string? RemotePoster { get; }

    public bool IsOwned => TitleId is not null;

    public string Subtitle => string.Join(" · ", new[]
    {
        Year?.ToString(CultureInfo.CurrentCulture),
        IsOwned ? Tr.T("Dans le catalogue") : Tr.T("Manquant"),
    }.Where(p => p is not null));

    public string? TmdbUrl => SourceId is { } id ? TmdbLinks.Title(Core.Titles.TitleKind.Movie, id) : null;

    [ObservableProperty]
    public partial Bitmap? Thumbnail { get; set; }
}

/// <summary>
/// Collections (sagas) du catalogue : présentation et décor TMDb, films possédés et films manquants,
/// mise à jour depuis TMDb, renommage et suppression.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "_cancel n'existe que pendant une opération et est libéré dans RunAsync.")]
public sealed partial class CollectionsViewModel : ObservableObject
{
    private readonly CollectionService _service;
    private readonly Dictionary<string, Bitmap?> _thumbnails = new(StringComparer.Ordinal);
    private List<CollectionRow> _all = [];
    private CancellationTokenSource? _cancel;

    public CollectionsViewModel(CollectionService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
        Reload();
        Selected = Collections.FirstOrDefault();
    }

    public ObservableCollection<CollectionRow> Collections { get; } = [];

    public ObservableCollection<CollectionFilm> Films { get; } = [];

    [ObservableProperty] public partial string Query { get; set; } = string.Empty;

    [ObservableProperty] public partial bool OnlyIncomplete { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand), nameof(RenameCommand), nameof(DeleteCommand))]
    public partial CollectionRow? Selected { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetail), nameof(Name), nameof(Overview), nameof(PosterPath), nameof(BackdropPath), nameof(TmdbUrl), nameof(Summary))]
    public partial CollectionDetail? Detail { get; private set; }

    [ObservableProperty] public partial string EditedName { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand), nameof(SyncCommand), nameof(RenameCommand), nameof(DeleteCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty] public partial string Message { get; set; } = string.Empty;

    /// <summary>Titre choisi parmi les films possédés : la fenêtre principale le sélectionne.</summary>
    public long? ChosenTitleId { get; private set; }

    /// <summary>Au moins une collection modifiée (renommée, supprimée, complétée) : la liste principale est relue.</summary>
    public bool Changed { get; private set; }

    public bool HasCollections => _all.Count > 0;

    public bool HasDetail => Detail is not null;

    public string Name => Detail?.Name ?? string.Empty;

    public string? Overview => Detail?.Overview;

    public string? PosterPath => _service.Resolve(Detail?.PosterRelativePath);

    public string? BackdropPath => _service.Resolve(Detail?.BackdropRelativePath);

    public string? TmdbUrl => Detail?.TmdbId is { } id ? TmdbLinks.Collection(id) : null;

    public string Summary
    {
        get
        {
            if (Detail is null)
            {
                return string.Empty;
            }

            var owned = Films.Count(f => f.IsOwned);
            var missing = Films.Count - owned;
            return Detail.FetchedAt is null
                ? Tr.F("{0} film(s) dans le catalogue · fiche TMDb pas encore téléchargée", owned)
                : missing == 0
                    ? Tr.F("Collection complète : {0} film(s)", owned)
                    : Tr.F("{0} film(s) dans le catalogue, {1} manquant(s)", owned, missing);
        }
    }

    public string CountText => Tr.F("{0} collection(s)", _all.Count);

    partial void OnQueryChanged(string value) => ApplyFilter();

    partial void OnOnlyIncompleteChanged(bool value) => ApplyFilter();

    partial void OnSelectedChanged(CollectionRow? value) => _ = LoadDetailAsync(value);

    public void Choose(CollectionFilm film)
    {
        ArgumentNullException.ThrowIfNull(film);
        ChosenTitleId = film.TitleId;
    }

    /// <summary>Lit la fiche TMDb de la collection choisie (présentation, films, affiche et décor), en remplaçant les images.</summary>
    [RelayCommand(CanExecute = nameof(CanEditSelected))]
    private async Task RefreshAsync()
    {
        if (Selected is not { } row)
        {
            return;
        }

        await RunAsync(async token =>
        {
            long? id = row.Summary.TmdbId is { } tmdbId
                ? await _service.RefreshAsync(row.Id, tmdbId, overwriteImages: true, token)
                : await _service.IdentifyAsync(row.Id, row.Name, token);
            Message = id is null
                ? Tr.F("Aucune collection TMDb ne correspond clairement à « {0} ». Renommez-la comme sur TMDb puis réessayez.", row.Name)
                : Tr.F("Collection « {0} » mise à jour depuis TMDb.", row.Name);
            ActivityJournal.Current.Write($"Collection mise à jour depuis TMDb : {row.Name}");
            Reload(id ?? row.Id);
        });
    }

    /// <summary>
    /// À l'ouverture : télécharge la fiche et les décors des collections liées à TMDb qui ne les ont pas encore
    /// (par exemple celles créées avant l'activation de la gestion des collections).
    /// </summary>
    public async Task CompleteMissingAsync()
    {
        if (IsBusy || _service.Repository.Stale().Count == 0)
        {
            return;
        }

        await RunAsync(async token =>
        {
            var result = await _service.SyncAsync(all: false, new Progress<string>(m => Message = m), token, identify: false);
            Message = Tr.F("{0} fiche(s) de collection téléchargée(s) depuis TMDb.", result.Updated) +
                      (result.Failed > 0 ? " " + Tr.F("{0} échec(s).", result.Failed) : string.Empty);
            ActivityJournal.Current.Write($"Collections complétées depuis TMDb : {result.Updated} fiche(s), {result.Failed} échec(s)");
            Reload(Selected?.Id);
        });
    }

    /// <summary>Identifie les collections sans fiche TMDb et télécharge les fiches et décors qui manquent.</summary>
    [RelayCommand(CanExecute = nameof(CanSync))]
    private async Task SyncAsync()
    {
        await RunAsync(async token =>
        {
            var result = await _service.SyncAsync(all: false, new Progress<string>(m => Message = m), token);
            Message = Tr.F("{0} fiche(s) téléchargée(s), {1} collection(s) identifiée(s) par leur nom, {2} introuvable(s), {3} échec(s).", result.Updated, result.Identified, result.NotFound, result.Failed);
            ActivityJournal.Current.Write($"Collections mises à jour depuis TMDb : {result.Updated} fiche(s), {result.Identified} identifiée(s), {result.NotFound} introuvable(s), {result.Failed} échec(s)");
            Reload(Selected?.Id);
        });
    }

    private bool CanSync() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanEditSelected))]
    private void Rename()
    {
        if (Selected is not { } row || string.IsNullOrWhiteSpace(EditedName) || EditedName.Trim() == row.Name)
        {
            return;
        }

        _service.Repository.Rename(row.Id, EditedName);
        ActivityJournal.Current.Write($"Collection renommée : {row.Name} → {EditedName.Trim()}");
        Changed = true;
        Reload(row.Id);
    }

    /// <summary>Supprime la collection choisie (après confirmation dans la fenêtre) ; ses films restent au catalogue.</summary>
    [RelayCommand(CanExecute = nameof(CanEditSelected))]
    private void Delete()
    {
        if (Selected is not { } row)
        {
            return;
        }

        _service.Repository.Delete(row.Id);
        ActivityJournal.Current.Write($"Collection supprimée : {row.Name}");
        Changed = true;
        Reload();
        Message = Tr.F("Collection « {0} » supprimée ; ses films restent dans le catalogue.", row.Name);
    }

    private bool CanEditSelected() => !IsBusy && Selected is not null;

    [RelayCommand]
    private void Cancel() => _cancel?.Cancel();

    private void Reload(long? selectId = null)
    {
        _all = [.. _service.Repository.List().Select(c => new CollectionRow(c, _service.Resolve(c.PosterRelativePath)))];
        OnPropertyChanged(nameof(HasCollections));
        OnPropertyChanged(nameof(CountText));
        ApplyFilter(selectId, reloadDetail: true);
    }

    private void ApplyFilter(long? selectId = null, bool reloadDetail = false)
    {
        var previous = Selected;
        selectId ??= Selected?.Id;
        var words = Query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Collections.Clear();
        foreach (var row in _all.Where(r => (!OnlyIncomplete || r.IsIncomplete) && words.All(w => r.Name.Contains(w, StringComparison.CurrentCultureIgnoreCase))))
        {
            Collections.Add(row);
        }

        Selected = Collections.FirstOrDefault(c => c.Id == selectId) ?? Collections.FirstOrDefault();
        if (reloadDetail && ReferenceEquals(previous, Selected) && Selected is { } current)
        {
            _ = LoadDetailAsync(current);
        }
    }

    private async Task LoadDetailAsync(CollectionRow? row)
    {
        Films.Clear();
        Detail = row is null ? null : _service.Repository.Get(row.Id);
        EditedName = Detail?.Name ?? string.Empty;
        if (Detail is null)
        {
            return;
        }

        // Les films de la saga dans l'ordre de sortie ; ceux du catalogue rangés dans la collection sans figurer
        // dans la fiche TMDb (coffret composé à la main) sont ajoutés à la suite.
        var members = _service.Repository.Members(Detail.Id);
        var byTmdb = members.Where(m => m.TmdbId is not null).GroupBy(m => m.TmdbId!.Value).ToDictionary(g => g.Key, g => g.First());
        var shown = new HashSet<long>();
        foreach (var part in Detail.Parts)
        {
            if (byTmdb.TryGetValue(part.SourceId, out var member))
            {
                shown.Add(member.TitleId);
                Films.Add(new CollectionFilm(member.TitleId, part.SourceId, member.Title, member.Year ?? part.Year, _service.Resolve(member.PosterRelativePath), null));
            }
            else
            {
                Films.Add(new CollectionFilm(null, part.SourceId, part.Title, part.Year, null, part.PosterPath));
            }
        }

        foreach (var member in members.Where(m => !shown.Contains(m.TitleId)))
        {
            Films.Add(new CollectionFilm(member.TitleId, member.TmdbId, member.Title, member.Year, _service.Resolve(member.PosterRelativePath), null));
        }

        OnPropertyChanged(nameof(Summary));
        var current = Detail;
        foreach (var film in Films.Where(f => f.RemotePoster is not null).ToList())
        {
            if (!ReferenceEquals(Detail, current))
            {
                return; // Une autre collection a été choisie entre-temps.
            }

            film.Thumbnail = await LoadThumbnailAsync(film.RemotePoster!);
        }
    }

    private async Task<Bitmap?> LoadThumbnailAsync(string path)
    {
        if (_thumbnails.TryGetValue(path, out var cached))
        {
            return cached;
        }

        Bitmap? bitmap;
        try
        {
            var bytes = await _service.DownloadThumbnailAsync(path);
            using var stream = new MemoryStream(bytes);
            bitmap = new Bitmap(stream);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TmdbUnavailableException or TmdbAuthenticationException or ArgumentException or InvalidOperationException or IOException)
        {
            // Affiche indisponible (hors ligne, pas de jeton) : le film reste listé avec son titre.
            bitmap = null;
        }

        _thumbnails[path] = bitmap;
        return bitmap;
    }

    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        IsBusy = true;
        _cancel = new CancellationTokenSource();
        try
        {
            await action(_cancel.Token);
            Changed = true;
        }
        catch (OperationCanceledException) when (_cancel.IsCancellationRequested)
        {
            Message = Tr.T("Interrompu.");
        }
        catch (OperationCanceledException)
        {
            // Délai d'attente du client HTTP dépassé (et non une interruption demandée).
            Message = Tr.T("TMDb ne répond pas. Vérifiez la connexion ou le proxy, puis réessayez.");
        }
        catch (TmdbAuthenticationException ex)
        {
            Message = ex.Message;
        }
        catch (Exception ex) when (ex is HttpRequestException or TmdbUnavailableException or InvalidDataException)
        {
            Message = ex.Message;
        }
        finally
        {
            IsBusy = false;
            _cancel.Dispose();
            _cancel = null;
        }
    }
}
