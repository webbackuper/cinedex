using System.Collections.ObjectModel;
using System.Globalization;
using Cinedex.App.Services;
using Cinedex.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cinedex.App.ViewModels;

/// <summary>Une sauvegarde de la liste : date, taille, contenu.</summary>
public sealed record DumpRow(DumpInfo Dump)
{
    public string Date => Dump.CreatedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    public string Details => string.Join(" · ", new[]
    {
        Dump.SizeBytes >= 1024 * 1024 ? Tr.F("{0:0.0} Mo", Dump.SizeBytes / 1024d / 1024d) : Tr.F("{0:0} Ko", Dump.SizeBytes / 1024d),
        Dump.HasDatabase ? Tr.T("base") : null,
        Dump.HasImages ? Tr.T("images") : null,
        Dump.HasPreferences ? Tr.T("préférences") : null,
        Dump.Titles is { } titles ? Tr.F("{0} titre(s)", titles) : null,
    }.Where(p => p is not null));
}

/// <summary>
/// Onglet Sauvegardes des réglages : contenu (base, images, préférences), dossier, planification et rotation,
/// sauvegarde immédiate, liste des sauvegardes et restauration. Les réglages s'enregistrent avec « Enregistrer » ;
/// les actions (sauvegarder, restaurer, supprimer) sont immédiates.
/// </summary>
public sealed partial class BackupsViewModel : ObservableObject
{
    private readonly BackupService _service;

    public BackupsViewModel(BackupService service)
    {
        _service = service;
        var settings = service.LoadSettings();
        IncludeDatabase = settings.IncludeDatabase;
        IncludeImages = settings.IncludeImages;
        IncludePreferences = settings.IncludePreferences;
        Folder = service.FolderOf(settings);
        Keep = settings.Keep;
        Schedules =
        [
            new(BackupSchedule.None, Tr.T("Jamais (à la demande seulement)")),
            new(BackupSchedule.AtStartup, Tr.T("À chaque démarrage de Cinedex")),
            new(BackupSchedule.Daily, Tr.T("Une fois par jour")),
            new(BackupSchedule.Weekly, Tr.T("Une fois par semaine")),
            new(BackupSchedule.Monthly, Tr.T("Une fois par mois")),
        ];
        SelectedSchedule = Schedules.Single(s => s.Value == settings.Schedule);
        LastRun = settings.LastRunUtc is { } last
            ? Tr.F("Dernière sauvegarde : {0}", last.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))
            : Tr.T("Aucune sauvegarde faite pour l'instant.");
        Reload();
    }

    public IReadOnlyList<Choice<BackupSchedule>> Schedules { get; }

    public ObservableCollection<DumpRow> Dumps { get; } = [];

    [ObservableProperty] public partial bool IncludeDatabase { get; set; }

    [ObservableProperty] public partial bool IncludeImages { get; set; }

    [ObservableProperty] public partial bool IncludePreferences { get; set; }

    [ObservableProperty] public partial string Folder { get; set; }

    [ObservableProperty] public partial decimal? Keep { get; set; }

    [ObservableProperty] public partial Choice<BackupSchedule> SelectedSchedule { get; set; }

    [ObservableProperty] public partial DumpRow? SelectedDump { get; set; }

    [ObservableProperty] public partial string LastRun { get; private set; }

    [ObservableProperty] public partial string? Message { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BackupNowCommand))]
    public partial bool IsBusy { get; private set; }

    /// <summary>Demande de confirmation (titre, message) ; fournie par la fenêtre.</summary>
    public Func<string, string, Task<bool>>? Confirm { get; set; }

    /// <summary>La fenêtre des réglages doit se fermer (restauration).</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Sauvegarde à restaurer, appliquée par l'application après la fermeture des réglages.</summary>
    public string? PendingRestore { get; private set; }

    private BackupSettings Current => new()
    {
        Folder = string.IsNullOrWhiteSpace(Folder) || Folder == _service.DefaultFolder ? null : Folder.Trim(),
        IncludeDatabase = IncludeDatabase,
        IncludeImages = IncludeImages,
        IncludePreferences = IncludePreferences,
        Schedule = SelectedSchedule.Value,
        Keep = (int)Math.Round(Keep ?? 10),
    };

    /// <summary>Enregistre les réglages (appelé par « Enregistrer » des réglages).</summary>
    public void Save() => _service.SaveSettings(Current);

    partial void OnFolderChanged(string value) => Reload();

    [RelayCommand(CanExecute = nameof(CanBackup))]
    private async Task BackupNowAsync()
    {
        IsBusy = true;
        Message = Tr.T("Sauvegarde en cours…");
        try
        {
            var options = Current;
            var progress = new Progress<string>(m => Message = m);
            var path = await Task.Run(() => _service.CreateDump(options, progress));
            ActivityJournal.Current.Write($"Sauvegarde créée : {path}");
            Message = Tr.F("Sauvegarde créée : {0}", path);
            LastRun = Tr.F("Dernière sauvegarde : {0}", DateTime.Now.ToString("g", CultureInfo.CurrentCulture));
            Reload();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            Message = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanBackup() => !IsBusy;

    /// <summary>Restaure la sauvegarde choisie : vérifiée ici, appliquée après la fermeture des réglages (le catalogue est rouvert).</summary>
    [RelayCommand]
    private async Task RestoreAsync()
    {
        if (SelectedDump is not { } row)
        {
            Message = Tr.T("Choisissez une sauvegarde dans la liste.");
            return;
        }

        var what = row.Dump.HasDatabase
            ? Tr.T("La base du catalogue est remplacée par celle de la sauvegarde (une copie de l'actuelle est gardée dans le dossier des sauvegardes).")
            : Tr.T("Cette sauvegarde ne contient pas de base : seules les images manquantes sont ajoutées.");
        if (Confirm is not null && !await Confirm(Tr.T("Restaurer une sauvegarde"), Tr.F("Restaurer la sauvegarde du {0} ?", row.Date) + "\n\n" + what))
        {
            return;
        }

        PendingRestore = row.Dump.Path;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (SelectedDump is not { } row)
        {
            return;
        }

        if (Confirm is not null && !await Confirm(Tr.T("Supprimer la sauvegarde"), Tr.F("Supprimer définitivement la sauvegarde du {0} ?", row.Date)))
        {
            return;
        }

        try
        {
            File.Delete(row.Dump.Path);
            ActivityJournal.Current.Write($"Sauvegarde supprimée : {row.Dump.Path}");
            Reload();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Message = ex.Message;
        }
    }

    public void UseDefaultFolder() => Folder = _service.DefaultFolder;

    private void Reload()
    {
        Dumps.Clear();
        foreach (var dump in _service.List(string.IsNullOrWhiteSpace(Folder) ? _service.DefaultFolder : Folder))
        {
            Dumps.Add(new DumpRow(dump));
        }
    }
}
