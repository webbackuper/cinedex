using System.Collections.ObjectModel;
using Cinedex.App.Services;
using Cinedex.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cinedex.Core.Localization;

namespace Cinedex.App.ViewModels;

/// <param name="isKnown">Le fichier est déjà rattaché à un titre : proposé, mais décoché.</param>
public sealed partial class CandidateRow(ScanCandidate candidate, bool isKnown = false) : ObservableObject
{
    public ScanCandidate Candidate { get; } = candidate;

    /// <summary>Déjà au catalogue : une nouvelle analyse du même dossier ne le recoche pas.</summary>
    public bool IsKnown { get; } = isKnown;

    public string Title => Candidate.BestTitle;

    public string Year => Candidate.BestYear?.ToString(System.Globalization.CultureInfo.CurrentCulture) ?? "?";

    public string Hint =>
        IsKnown ? "déjà au catalogue"
        : Candidate.Parsed.IsEpisode ? "épisode (géré avec les séries)"
        : Candidate.Nfo?.TmdbId is not null ? ".nfo : TMDb"
        : Candidate.ImdbId is not null ? "identifiant IMDb"
        : string.Empty;

    public string Path => Candidate.FullPath;

    [ObservableProperty]
    public partial bool IsSelected { get; set; } = !isKnown && !candidate.Parsed.IsEpisode;
}

/// <summary>Ajout depuis le disque : choix du dossier, analyse, sélection, import avec progression.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "_cancel n'existe que pendant une opération et est libéré dans RunAsync.")]
public sealed partial class ImportFolderViewModel(LibraryImportService import, ScanSettings? defaults = null) : ObservableObject
{
    private readonly ScanSettings _defaults = defaults ?? new ScanSettings();

    /// <summary>Extensions retenues (réglages), pour le filtre du sélecteur de fichiers.</summary>
    public IReadOnlyList<string> VideoExtensions => _defaults.Extensions;

    private CancellationTokenSource? _cancel;

    public ObservableCollection<CandidateRow> Candidates { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    public partial string Folder { get; set; } = string.Empty;

    [ObservableProperty] public partial bool Recursive { get; set; } = defaults?.Recursive ?? true;

    [ObservableProperty] public partial bool IgnoreBracketed { get; set; } = defaults?.IgnoreBracketed ?? true;

    [ObservableProperty] public partial bool UseTmdb { get; set; } = defaults?.UseTmdb ?? false;

    [ObservableProperty] public partial bool UseFirstMatch { get; set; }

    [ObservableProperty] public partial bool ReadProperties { get; set; } = defaults?.ReadProperties ?? true;

    [ObservableProperty] public partial bool WriteNfo { get; set; } = defaults?.WriteNfo ?? false;

    /// <summary>Prendre en compte le fichier .nfo de chaque vidéo s'il existe (coché par défaut).</summary>
    [ObservableProperty] public partial bool ReadNfo { get; set; } = true;

    [ObservableProperty] public partial bool RememberFolder { get; set; } = true;

    [ObservableProperty] public partial string NoiseWords { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand), nameof(ImportCommand))]
    public partial bool IsBusy { get; set; }

    /// <summary>Progression de l'import : fichiers traités et total (0 pendant l'analyse : barre animée).</summary>
    [ObservableProperty]
    public partial int Done { get; private set; }

    [ObservableProperty]
    public partial int Total { get; private set; }

    public bool IsIndeterminate => IsBusy && Total == 0;

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsIndeterminate));

    partial void OnTotalChanged(int value) => OnPropertyChanged(nameof(IsIndeterminate));

    [ObservableProperty] public partial string Message { get; set; } = "Choisissez un dossier à analyser, ou des fichiers vidéo (bouton ou glisser-déposer).";

    private ScanOptions CurrentScanOptions => _defaults.ToScanOptions() with
    {
        Recursive = Recursive,
        IgnoreBracketed = IgnoreBracketed,
        ReadNfo = ReadNfo,
        NoiseWords = [.. _defaults.NoiseWords, .. TitleEditorViewModel.SplitList(NoiseWords)],
    };

    /// <summary>
    /// Ajoute à la liste des fichiers choisis un par un ou déposés (un dossier déposé est analysé).
    /// La liste existante est conservée : on peut combiner un dossier et quelques fichiers isolés.
    /// </summary>
    public async Task AddPathsAsync(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        await RunAsync(async token =>
        {
            var known = new HashSet<string>(Candidates.Select(c => c.Path), StringComparer.OrdinalIgnoreCase);
            var attached = import.AttachedPaths();
            var found = await import.FromPathsAsync(paths, CurrentScanOptions, token);
            var added = 0;
            foreach (var candidate in found.Where(c => known.Add(c.FullPath)))
            {
                Candidates.Add(new CandidateRow(candidate, attached.Contains(candidate.FullPath)));
                added++;
            }

            Message = added == 0
                ? "Aucun nouveau fichier vidéo parmi les éléments choisis (déjà au catalogue ou pas une vidéo)."
                : Tr.F("{0} fichier(s) ajouté(s) à la liste ({1} au total). Décochez ceux à ignorer, puis importez.", added, Candidates.Count);
        });
    }

    public bool Imported { get; private set; }

    /// <summary>Dernier import : nombre de fichiers et résultat (journal des actions).</summary>
    public (int Files, LibraryImportResult Result)? LastResult { get; private set; }

    /// <summary>Titres de cet import dont la fiche TMDb reste à choisir (proposés à la fermeture de la fenêtre).</summary>
    public int ToReview { get; private set; }

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        Candidates.Clear();
        await RunAsync(async token =>
        {
            Message = "Analyse du dossier...";
            var found = await import.ScanAsync(Folder, CurrentScanOptions, token);
            var attached = import.AttachedPaths();
            foreach (var candidate in found)
            {
                Candidates.Add(new CandidateRow(candidate, attached.Contains(candidate.FullPath)));
            }

            var fresh = Candidates.Count(c => !c.IsKnown);
            var again = Candidates.Count - fresh;
            Message = (fresh, again) switch
            {
                (0, 0) => "Aucun nouveau fichier vidéo (les fichiers déjà au catalogue sont ignorés).",
                (0, _) => Tr.F("Aucun nouveau fichier : les {0} fichier(s) retrouvé(s) sont déjà au catalogue, et restent décochés.", again),
                (_, 0) => Tr.F("{0} nouveau(x) fichier(s). Décochez ceux à ignorer, puis importez.", fresh),
                _ => Tr.F("{0} nouveau(x) fichier(s) coché(s) ; {1} déjà au catalogue, laissé(s) décoché(s). Puis importez.", fresh, again),
            };
        });
    }

    private bool CanScan() => !IsBusy && Directory.Exists(Folder);

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportAsync()
    {
        // Case décochée après l'analyse : les .nfo déjà lus sont ignorés à l'import.
        var selected = Candidates.Where(c => c.IsSelected).Select(c => ReadNfo ? c.Candidate : c.Candidate with { Nfo = null }).ToList();
        await RunAsync(async token =>
        {
            if (RememberFolder && Directory.Exists(Folder))
            {
                // Avant l'import, pour que les fichiers soient enregistrés relativement à ce dossier.
                import.RememberFolder(Folder);
            }

            var options = new LibraryImportOptions { UseTmdb = UseTmdb, UseFirstMatch = UseFirstMatch, ReadTechnicalProperties = ReadProperties, WriteNfo = WriteNfo, ReadNfo = ReadNfo };
            Total = selected.Count;
            var done = 0;
            var progress = new Progress<string>(m =>
            {
                Message = m;
                Done = Math.Min(++done, Total);
            });
            var result = await import.ImportAsync(selected, options, progress, token);
            Imported = true;
            ToReview += result.ToReview;
            Candidates.Clear();
            var summary = Tr.F("{0} ajouté(s) avec TMDb, {1} sans fiche TMDb (dont {2} complété(s) par leur .nfo), {3} déjà présent(s)", result.Added, result.WithoutMetadata, result.FromNfo, result.Linked);
            if (result.ToReview > 0)
            {
                summary += Tr.F(", {0} à vérifier : les choix vous seront proposés à la fermeture de cette fenêtre", result.ToReview);
            }

            if (result.Episodes > 0)
            {
                summary += Tr.F(", {0} épisode(s) laissé(s) pour les séries", result.Episodes);
            }

            if (result.Failed > 0)
            {
                summary += Tr.F(", {0} échec(s)", result.Failed);
            }

            // Les fichiers signalés sont ceux à reprendre à la main : en cacher en silence serait pire que
            // d'allonger le compte rendu, qui se sélectionne et se copie.
            const int shown = 20;
            string[] rest = result.Messages.Count > shown
                ? [Tr.F("… et {0} autre(s)", result.Messages.Count - shown)]
                : [];
            Message = string.Join("\n", [summary + ".", .. result.Messages.Take(shown), .. rest]);
            LastResult = (selected.Count, result);
        });
    }

    private bool CanImport() => !IsBusy;

    [RelayCommand]
    private void Cancel() => _cancel?.Cancel();

    [RelayCommand]
    private void SelectAll(bool value)
    {
        foreach (var row in Candidates)
        {
            row.IsSelected = value;
        }
    }

    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        IsBusy = true;
        Done = 0;
        Total = 0;
        _cancel = new CancellationTokenSource();
        try
        {
            await action(_cancel.Token);
        }
        catch (ApiLimitAbortedException ex)
        {
            Message = ex.Message + " " + Tr.T("Les fichiers déjà traités restent dans le catalogue.");
            Imported = true;
        }
        catch (OperationCanceledException)
        {
            Message = "Interrompu.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
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
