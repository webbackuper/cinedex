using System.Collections.ObjectModel;
using Cinedex.App.Services;
using Cinedex.Data;
using Cinedex.Media;
using Cinedex.Metadata.Tmdb;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cinedex.Core.Localization;

namespace Cinedex.App.ViewModels;

/// <summary>Ligne d'épisode : cocher « vu » l'enregistre aussitôt.</summary>
public sealed partial class EpisodeRow : ObservableObject
{
    private readonly Action<EpisodeRow, bool> _onSeen;
    private bool _loading = true;

    public EpisodeRow(EpisodeView episode, Action<EpisodeRow, bool> onSeen)
    {
        Episode = episode;
        _onSeen = onSeen;
        IsSeen = episode.IsSeen;
        _loading = false;
    }

    public EpisodeView Episode { get; }

    public string Code => Episode.Code;

    public string Name => Episode.Name ?? Tr.F("Épisode {0}", Episode.Number);

    public string Aired => Episode.AirDate?.ToString("d", System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty;

    public bool HasFile => Episode.FilePath is not null;

    [ObservableProperty]
    public partial bool IsSeen { get; set; }

    partial void OnIsSeenChanged(bool value)
    {
        if (!_loading)
        {
            _onSeen(this, value);
        }
    }
}

/// <summary>Saisons et épisodes d'une série : vu par épisode, par saison ou pour toute la série, lecture, mise à jour TMDb.</summary>
public sealed partial class SeriesViewModel : ObservableObject
{
    private readonly long _titleId;
    private readonly long? _tmdbId;
    private readonly SeriesRepository _series;
    private readonly MetadataService? _metadata;
    private readonly MediaFileRepository? _files;
    private readonly SettingsStore? _settings;

    public SeriesViewModel(long titleId, string title, long? tmdbId, SeriesRepository series, MetadataService? metadata, MediaFileRepository? files, SettingsStore? settings)
    {
        _titleId = titleId;
        _tmdbId = tmdbId;
        _series = series;
        _metadata = metadata;
        _files = files;
        _settings = settings;
        Title = title;
        Reload();
    }

    public string Title { get; }

    public ObservableCollection<SeasonView> Seasons { get; } = [];

    public ObservableCollection<EpisodeRow> Episodes { get; } = [];

    public bool CanSync => _metadata is not null && _tmdbId is not null;

    [ObservableProperty]
    public partial SeasonView? SelectedSeason { get; set; }

    [ObservableProperty]
    public partial EpisodeRow? SelectedEpisode { get; set; }

    [ObservableProperty]
    public partial string Message { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>Vrai si une modification a eu lieu (la fenêtre principale se rafraîchit alors).</summary>
    public bool Changed { get; private set; }

    private bool _suppressReload;

    partial void OnSelectedSeasonChanged(SeasonView? value)
    {
        if (!_suppressReload)
        {
            LoadEpisodes();
        }
    }

    [RelayCommand]
    private async Task SyncAsync()
    {
        if (!CanSync)
        {
            Message = "Liez d'abord la série à TMDb (bouton nuage de la fiche).";
            return;
        }

        IsBusy = true;
        Message = "Mise à jour des saisons et épisodes depuis TMDb...";
        try
        {
            var added = await _metadata!.SyncEpisodesAsync(_titleId, _tmdbId!.Value);
            Changed = true;
            Reload();
            Message = Tr.F("{0} nouvel(s) épisode(s).", added);
        }
        catch (Exception ex) when (ex is TmdbAuthenticationException or TmdbUnavailableException or HttpRequestException or KeyNotFoundException)
        {
            Message = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void SeasonSeen(bool seen)
    {
        if (SelectedSeason is { } season)
        {
            _series.SetSeasonSeen(season.Id, seen);
            Changed = true;
            Reload(season.Id);
        }
    }

    [RelayCommand]
    private void SeriesSeen(bool seen)
    {
        _series.SetSeriesSeen(_titleId, seen);
        Changed = true;
        Reload(SelectedSeason?.Id);
    }

    /// <summary>
    /// Question à plusieurs réponses, posée par la fenêtre de la série. Sans elle (tests), un fichier absent
    /// est simplement signalé.
    /// </summary>
    public MediaLocator.Ask? Ask { get; set; }

    /// <summary>Racines où chercher un fichier absent ; nul pour celles du système.</summary>
    public Func<IReadOnlyList<string>>? MediaRoots { get; set; }

    [RelayCommand]
    private async Task PlayEpisodeAsync()
    {
        if (SelectedEpisode is not { Episode.FilePath: { } recorded } chosen)
        {
            Message = "Pas de fichier pour cet épisode.";
            return;
        }

        // Le fichier d'un épisode se vérifie comme celui d'un film : s'il manque, on demande son disque.
        var path = recorded;
        if (!File.Exists(path))
        {
            var located = Ask is null
                ? null
                : await MediaLocator.LocateAsync(() => [new MediaCandidate(recorded, null)], Ask, MediaRoots);
            if (located is null)
            {
                Message = Tr.T("Fichier introuvable : le disque est peut-être débranché.");
                return;
            }

            path = located.Path;
        }

        try
        {
            Player.Play(path, _settings?.Get(MediaContext.PlayerKey), _settings?.Get(MediaContext.PlayerArgsKey));
            if (_settings?.Get(MediaContext.MarkSeenOnPlayKey) != "false")
            {
                chosen.IsSeen = true;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Message = ex.Message;
        }
    }

    /// <summary>Rattache aux épisodes les fichiers S01E02 d'un dossier (convention Kodi).</summary>
    public int AttachFolder(string folder)
    {
        if (_files is null)
        {
            return 0;
        }

        var attached = 0;
        foreach (var candidate in LibraryScanner.Scan(folder, new ScanOptions { MinimumBytes = 0 }).Where(c => c.Parsed.IsEpisode))
        {
            var episodeId = _series.FindEpisode(_titleId, candidate.Parsed.Season!.Value, candidate.Parsed.Episode!.Value)
                ?? _series.EnsureEpisode(_titleId, candidate.Parsed.Season.Value, candidate.Parsed.Episode.Value);
            _files.AttachEpisode(episodeId, candidate.FullPath, candidate.SizeBytes, candidate.Parsed.Part ?? 1);
            attached++;
        }

        Changed = true;
        Reload(SelectedSeason?.Id);
        Message = attached == 0 ? "Aucun épisode reconnu (noms attendus : S01E02 ou 1x02)." : Tr.F("{0} fichier(s) d'épisode rattaché(s).", attached);
        return attached;
    }

    private void Reload(long? keepSeasonId = null)
    {
        Seasons.Clear();
        foreach (var season in _series.Seasons(_titleId))
        {
            Seasons.Add(season);
        }

        SelectedSeason = Seasons.FirstOrDefault(s => s.Id == keepSeasonId) ?? Seasons.FirstOrDefault(s => s.Number > 0) ?? Seasons.FirstOrDefault();
        if (Seasons.Count == 0)
        {
            Message = CanSync
                ? "Aucune saison enregistrée : cliquez sur « Mettre à jour depuis TMDb »."
                : "Aucune saison : liez la série à TMDb ou rattachez un dossier d'épisodes.";
        }
    }

    private void LoadEpisodes()
    {
        Episodes.Clear();
        if (SelectedSeason is null)
        {
            return;
        }

        foreach (var episode in _series.Episodes(SelectedSeason.Id))
        {
            Episodes.Add(new EpisodeRow(episode, OnEpisodeSeen));
        }
    }

    private void OnEpisodeSeen(EpisodeRow row, bool seen)
    {
        _series.SetEpisodesSeen([row.Episode.Id], seen, DateTimeOffset.Now);
        Changed = true;
        var seasonId = SelectedSeason?.Id;
        var refreshed = _series.Seasons(_titleId);
        for (var i = 0; i < Seasons.Count; i++)
        {
            if (refreshed.FirstOrDefault(s => s.Id == Seasons[i].Id) is { } updated && updated != Seasons[i])
            {
                // Remplace l'élément pour mettre à jour le compteur « x/y vus » sans recharger les épisodes.
                var wasSelected = Seasons[i].Id == seasonId;
                Seasons[i] = updated;
                if (wasSelected)
                {
                    _suppressReload = true;
                    SelectedSeason = updated;
                    _suppressReload = false;
                }
            }
        }
    }
}
