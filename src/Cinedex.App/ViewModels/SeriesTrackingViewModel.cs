using System.Collections.ObjectModel;
using System.Globalization;
using Cinedex.App.Services;
using Cinedex.Core.Localization;
using Cinedex.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cinedex.App.ViewModels;

/// <summary>Une série dans le suivi : progression, prochain épisode à regarder, prochaine diffusion.</summary>
public sealed class TrackingRow(SeriesTracking tracking, string? posterPath)
{
    public SeriesTracking Tracking { get; } = tracking;

    public string Title => Tracking.Title;

    public string? PosterPath { get; } = posterPath;

    public double Percent => Tracking.Ratio * 100;

    public string Progress => Tracking.Aired == 0
        ? Tr.T("Aucun épisode diffusé connu")
        : Tr.F("{0} épisode(s) vu(s) sur {1} diffusé(s)", Tracking.Seen, Tracking.Aired);

    public string Status => Tracking.Status switch
    {
        "continuing" => Tr.T("En cours de diffusion"),
        "ended" => Tr.T("Terminée"),
        "cancelled" => Tr.T("Annulée"),
        _ => string.Empty,
    };

    public bool HasNext => Tracking.NextToWatch is not null;

    public string NextText => Tracking.NextToWatch is { } next
        ? Tr.F("À regarder : {0}", Describe(next))
        : Tracking.Aired > 0 ? Tr.T("À jour") : string.Empty;

    public string AiringText => Tracking.NextAiring is { } airing
        ? Tr.F("Prochaine diffusion : {0} le {1}", Describe(airing), airing.AirDate?.ToString("d", CultureInfo.CurrentCulture) ?? "?")
        : string.Empty;

    internal static string Describe(EpisodeView episode) =>
        string.IsNullOrWhiteSpace(episode.Name) ? episode.Code : $"{episode.Code} · {episode.Name}";
}

/// <summary>Un épisode du calendrier des prochaines diffusions.</summary>
public sealed record UpcomingRow(UpcomingEpisode Upcoming)
{
    public string Date => Upcoming.Episode.AirDate?.ToString("ddd d MMM", CultureInfo.CurrentCulture) ?? "?";

    public string Series => Upcoming.Series;

    public string Episode => TrackingRow.Describe(Upcoming.Episode);
}

/// <summary>
/// Suivi des séries : pour chaque série du catalogue, épisodes vus sur épisodes diffusés, prochain épisode à
/// regarder (marquable vu d'un clic) et prochaine diffusion ; calendrier des diffusions des 60 prochains jours ;
/// actualisation des épisodes depuis TMDb pour les séries en cours.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "_cancel n'existe que pendant une opération et est libéré à sa fin.")]
public sealed partial class SeriesTrackingViewModel : ObservableObject
{
    public const int UpcomingDays = 60;

    private readonly SeriesRepository _series;
    private readonly MetadataService? _metadata;
    private readonly Func<string?, string?> _resolve;
    private readonly Func<DateOnly> _today;
    private CancellationTokenSource? _cancel;
    private List<TrackingRow> _all = [];

    public SeriesTrackingViewModel(SeriesRepository series, MetadataService? metadata, Func<string?, string?> resolvePoster, Func<DateOnly>? today = null)
    {
        _series = series;
        _metadata = metadata;
        _resolve = resolvePoster;
        _today = today ?? (() => DateOnly.FromDateTime(DateTime.Now));
        Filters = [new(0, Tr.T("Séries à regarder")), new(1, Tr.T("Séries en cours de diffusion")), new(2, Tr.T("Toutes les séries"))];
        SelectedFilter = Filters[0];
        Reload();
    }

    public IReadOnlyList<Choice<int>> Filters { get; }

    public ObservableCollection<TrackingRow> Rows { get; } = [];

    public ObservableCollection<UpcomingRow> Upcoming { get; } = [];

    [ObservableProperty] public partial Choice<int> SelectedFilter { get; set; }

    [ObservableProperty] public partial TrackingRow? Selected { get; set; }

    [ObservableProperty] public partial string? Message { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshFromTmdbCommand))]
    public partial bool IsBusy { get; private set; }

    /// <summary>Série choisie : la fenêtre principale l'affiche.</summary>
    public long? ChosenTitleId { get; private set; }

    /// <summary>Des épisodes ont été marqués vus ou ajoutés : la liste principale est relue.</summary>
    public bool Changed { get; private set; }

    public bool CanRefresh => _metadata is not null;

    public string Summary
    {
        get
        {
            var toWatch = _all.Count(r => r.Tracking.ToWatch > 0);
            var episodes = _all.Sum(r => r.Tracking.ToWatch);
            return toWatch == 0
                ? Tr.F("{0} série(s), toutes à jour.", _all.Count)
                : Tr.F("{0} série(s) · {1} à regarder ({2} épisode(s) en attente)", _all.Count, toWatch, episodes);
        }
    }

    partial void OnSelectedFilterChanged(Choice<int> value) => ApplyFilter();

    /// <summary>Marque vu le prochain épisode de la série, puis passe au suivant.</summary>
    [RelayCommand]
    private void MarkNextSeen(TrackingRow? row)
    {
        if (row?.Tracking.NextToWatch is not { } next)
        {
            return;
        }

        _series.SetEpisodesSeen([next.Id], seen: true, DateTimeOffset.Now);
        ActivityJournal.Current.Write($"Épisode vu : {row.Title} {next.Code}");
        Changed = true;
        Reload();
        Selected = Rows.FirstOrDefault(r => r.Tracking.TitleId == row.Tracking.TitleId);
    }

    public void Choose(TrackingRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        ChosenTitleId = row.Tracking.TitleId;
    }

    /// <summary>Relit saisons et épisodes sur TMDb pour les séries liées et en cours (nouvelles diffusions annoncées).</summary>
    [RelayCommand(CanExecute = nameof(CanRefreshNow))]
    private async Task RefreshFromTmdbAsync()
    {
        if (_metadata is null)
        {
            return;
        }

        var targets = _all.Where(r => r.Tracking.TmdbId is not null && r.Tracking.Status is "continuing" or "unknown" or null).ToList();
        IsBusy = true;
        _cancel = new CancellationTokenSource();
        var added = 0;
        var failed = 0;
        try
        {
            for (var i = 0; i < targets.Count; i++)
            {
                Message = Tr.F("Actualisation depuis TMDb : {0} / {1} — {2}", i + 1, targets.Count, targets[i].Title);
                try
                {
                    added += await _metadata.SyncEpisodesAsync(targets[i].Tracking.TitleId, targets[i].Tracking.TmdbId!.Value, cancellationToken: _cancel.Token);
                }
                catch (Exception ex) when (ex is HttpRequestException or Cinedex.Metadata.Tmdb.TmdbUnavailableException)
                {
                    failed++;
                }
            }

            Message = Tr.F("{0} série(s) actualisée(s), {1} nouvel(s) épisode(s), {2} échec(s).", targets.Count - failed, added, failed);
            ActivityJournal.Current.Write($"Suivi des séries actualisé depuis TMDb : {added} nouvel(s) épisode(s)");
            Changed |= added > 0;
        }
        catch (OperationCanceledException)
        {
            Message = Tr.T("Interrompu.");
        }
        catch (Cinedex.Metadata.Tmdb.TmdbAuthenticationException ex)
        {
            Message = ex.Message;
        }
        finally
        {
            IsBusy = false;
            _cancel.Dispose();
            _cancel = null;
            Reload();
        }
    }

    private bool CanRefreshNow() => CanRefresh && !IsBusy;

    [RelayCommand]
    private void Cancel() => _cancel?.Cancel();

    private void Reload()
    {
        var today = _today();
        _all = [.. _series.Tracking(today).Select(t => new TrackingRow(t, _resolve(t.PosterRelativePath)))];
        Upcoming.Clear();
        foreach (var upcoming in _series.Upcoming(today, UpcomingDays))
        {
            Upcoming.Add(new UpcomingRow(upcoming));
        }

        ApplyFilter();
        OnPropertyChanged(nameof(Summary));
    }

    private void ApplyFilter()
    {
        Rows.Clear();
        foreach (var row in _all.Where(r => SelectedFilter.Value switch
        {
            0 => r.Tracking.ToWatch > 0,
            1 => r.Tracking.IsRunning,
            _ => true,
        }))
        {
            Rows.Add(row);
        }
    }
}
