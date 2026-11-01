using System.Collections.ObjectModel;
using Bobine.App.Services;
using Bobine.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bobine.App.ViewModels;

public sealed partial class CandidateRow(ScanCandidate candidate) : ObservableObject
{
    public ScanCandidate Candidate { get; } = candidate;

    public string Title => Candidate.BestTitle;

    public string Year => Candidate.BestYear?.ToString(System.Globalization.CultureInfo.CurrentCulture) ?? "?";

    public string Hint =>
        Candidate.Parsed.IsEpisode ? "épisode (géré avec les séries)"
        : Candidate.Nfo?.TmdbId is not null ? ".nfo : TMDb"
        : Candidate.ImdbId is not null ? "identifiant IMDb"
        : string.Empty;

    public string Path => Candidate.FullPath;

    [ObservableProperty]
    public partial bool IsSelected { get; set; } = !candidate.Parsed.IsEpisode;
}

/// <summary>Ajout depuis le disque : choix du dossier, analyse, sélection, import avec progression.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "_cancel n'existe que pendant une opération et est libéré dans RunAsync.")]
public sealed partial class ImportFolderViewModel(LibraryImportService import) : ObservableObject
{
    private CancellationTokenSource? _cancel;

    public ObservableCollection<CandidateRow> Candidates { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    public partial string Folder { get; set; } = string.Empty;

    [ObservableProperty] public partial bool Recursive { get; set; } = true;

    [ObservableProperty] public partial bool UseTmdb { get; set; }

    [ObservableProperty] public partial bool UseFirstMatch { get; set; } = true;

    [ObservableProperty] public partial bool ReadProperties { get; set; } = true;

    [ObservableProperty] public partial bool WriteNfo { get; set; }

    [ObservableProperty] public partial bool RememberFolder { get; set; } = true;

    [ObservableProperty] public partial string NoiseWords { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand), nameof(ImportCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty] public partial string Message { get; set; } = "Choisissez un dossier contenant des films, puis lancez l'analyse.";

    public bool Imported { get; private set; }

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        Candidates.Clear();
        await RunAsync(async token =>
        {
            Message = "Analyse du dossier...";
            var options = new ScanOptions { Recursive = Recursive, NoiseWords = TitleEditorViewModel.SplitList(NoiseWords) };
            var found = await import.ScanAsync(Folder, options, token);
            foreach (var candidate in found)
            {
                Candidates.Add(new CandidateRow(candidate));
            }

            Message = found.Count == 0
                ? "Aucun nouveau fichier vidéo (les fichiers déjà au catalogue sont ignorés)."
                : $"{found.Count} nouveau(x) fichier(s). Décochez ceux à ignorer, puis importez.";
        });
    }

    private bool CanScan() => !IsBusy && Directory.Exists(Folder);

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportAsync()
    {
        var selected = Candidates.Where(c => c.IsSelected).Select(c => c.Candidate).ToList();
        await RunAsync(async token =>
        {
            if (RememberFolder)
            {
                // Avant l'import, pour que les fichiers soient enregistrés relativement à ce dossier.
                import.RememberFolder(Folder);
            }

            var options = new LibraryImportOptions { UseTmdb = UseTmdb, UseFirstMatch = UseFirstMatch, ReadTechnicalProperties = ReadProperties, WriteNfo = WriteNfo };
            var progress = new Progress<string>(m => Message = m);
            var result = await import.ImportAsync(selected, options, progress, token);
            Imported = true;
            Candidates.Clear();
            var summary = $"{result.Added} ajouté(s) avec TMDb, {result.WithoutMetadata} sans fiche TMDb (dont {result.FromNfo} complété(s) par leur .nfo), {result.Linked} déjà présent(s)";
            if (result.Episodes > 0)
            {
                summary += $", {result.Episodes} épisode(s) laissé(s) pour les séries";
            }

            if (result.Failed > 0)
            {
                summary += $", {result.Failed} échec(s)";
            }

            Message = string.Join("\n", [summary + ".", .. result.Messages.Take(5)]);
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
        _cancel = new CancellationTokenSource();
        try
        {
            await action(_cancel.Token);
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
