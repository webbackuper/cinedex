using System.Collections.ObjectModel;
using System.Globalization;
using Cinedex.App.Services;
using Cinedex.Core.Localization;
using Cinedex.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cinedex.App.ViewModels;

/// <summary>Une ligne de la liste des doublons : le groupe, puis chacun de ses titres.</summary>
public sealed record DuplicateRow(long? TitleId, string Text, bool IsHeader);

/// <summary>
/// Onglet Maintenance des réglages : renommage du catalogue ouvert, nettoyages, doublons, renumérotation,
/// contrôle, compactage et sauvegarde. Chaque opération qui modifie la base est précédée d'une sauvegarde.
/// </summary>
public sealed partial class MaintenanceViewModel : ObservableObject
{
    private readonly MaintenanceRepository _repository;
    private readonly CatalogManager _catalogs;
    private readonly string _backupsDirectory;
    private readonly string _imagesDirectory;

    public MaintenanceViewModel(MaintenanceRepository repository, CatalogManager catalogs, string catalogName, string backupsDirectory, string imagesDirectory)
    {
        _repository = repository;
        _catalogs = catalogs;
        CatalogName = catalogName;
        NewName = catalogName;
        _backupsDirectory = backupsDirectory;
        _imagesDirectory = imagesDirectory;
        RefreshInfo();
    }

    public string CatalogName { get; }

    public string BackupsDirectory => _backupsDirectory;

    /// <summary>Tri actuel de la liste principale (libellé, titres dans cet ordre), fourni par la fenêtre principale.</summary>
    public (string Label, Func<IReadOnlyList<long>> Ids)? CurrentOrder
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged(nameof(RenumberLabel));
        }
    }

    /// <summary>Demande de confirmation (titre, message) avant une opération ; fournie par la fenêtre.</summary>
    public Func<string, string, Task<bool>>? Confirm { get; set; }

    /// <summary>La fenêtre des réglages doit se fermer (renommage du catalogue, titre à afficher).</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Nouveau nom du catalogue ouvert, appliqué par l'application après la fermeture des réglages.</summary>
    public string? PendingRename { get; private set; }

    /// <summary>Titre à afficher dans la liste principale après la fermeture des réglages.</summary>
    public long? PendingTitleId { get; private set; }

    /// <summary>La base a été modifiée : la liste principale est relue.</summary>
    public bool Changed { get; private set; }

    public ObservableCollection<DuplicateRow> Duplicates { get; } = [];

    [ObservableProperty] public partial string NewName { get; set; }

    [ObservableProperty] public partial string InfoText { get; private set; } = string.Empty;

    [ObservableProperty] public partial string? Message { get; set; }

    [ObservableProperty] public partial int UnusedPeople { get; private set; }

    [ObservableProperty] public partial DuplicateRow? SelectedDuplicate { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveUnusedPeopleCommand), nameof(MergePeopleCommand), nameof(FindDuplicatesCommand), nameof(RenumberCommand),
        nameof(RemoveEmptyLabelsCommand), nameof(RemoveUnusedImagesCommand), nameof(CheckIntegrityCommand), nameof(OptimizeCommand), nameof(BackupCommand), nameof(RenameCommand), nameof(PurgeCatalogCommand))]
    public partial bool IsBusy { get; private set; }

    public string UnusedPeopleLabel => Tr.F("Retirer les acteurs inutilisés ({0})", UnusedPeople);

    public string RenumberLabel => CurrentOrder is { } order
        ? Tr.F("Renuméroter les films selon le tri actuel ({0})", order.Label)
        : Tr.T("Renuméroter les films par titre");

    partial void OnUnusedPeopleChanged(int value) => OnPropertyChanged(nameof(UnusedPeopleLabel));

    private bool CanRun() => !IsBusy;

    /// <summary>Renomme le catalogue ouvert : vérifié ici, appliqué après la fermeture des réglages (la base est rouverte).</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RenameAsync()
    {
        var target = Cinedex.Core.AppPaths.SanitizeName(NewName ?? string.Empty);
        if (string.IsNullOrWhiteSpace(NewName) || target == CatalogName)
        {
            Message = Tr.T("Saisissez un nouveau nom de catalogue.");
            return;
        }

        if (_catalogs.Exists(target) || _catalogs.List().Any(c => string.Equals(c.Name, target, StringComparison.OrdinalIgnoreCase)))
        {
            Message = Tr.F("Le catalogue « {0} » existe déjà.", target);
            return;
        }

        if (!await AskAsync(Tr.T("Renommer le catalogue"), Tr.F("Renommer le catalogue « {0} » en « {1} » ? Les réglages se ferment et le catalogue est rouvert sous son nouveau nom.", CatalogName, target)))
        {
            return;
        }

        PendingRename = target;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RemoveUnusedPeopleAsync() => ModifyAsync(
        Tr.T("Retirer les acteurs inutilisés"),
        Tr.F("Retirer les {0} personnes qui ne figurent plus dans aucun film ?", UnusedPeople),
        "acteurs-inutilises",
        () =>
        {
            var removed = _repository.RemoveUnusedPeople();
            ActivityJournal.Current.Write($"Maintenance : {removed} personne(s) inutilisée(s) retirée(s)");
            return Tr.F("{0} personne(s) retirée(s).", removed);
        });

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task MergePeopleAsync() => ModifyAsync(
        Tr.T("Fusionner les doublons d'acteurs et de réalisateurs"),
        Tr.T("Regrouper sur une seule fiche les personnes au nom identique (accents et majuscules ignorés) ? Les homonymes dont les fiches TMDb ou IMDb diffèrent restent distincts."),
        "doublons-personnes",
        () =>
        {
            var merged = _repository.MergeDuplicatePeople();
            ActivityJournal.Current.Write($"Maintenance : {merged} doublon(s) de personnes fusionné(s)");
            return merged == 0 ? Tr.T("Aucun doublon de personne trouvé.") : Tr.F("{0} fiche(s) en double fusionnée(s).", merged);
        });

    /// <summary>Cherche les films en double (rien n'est supprimé) ; un titre de la liste s'affiche dans le catalogue.</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task FindDuplicatesAsync()
    {
        await RunAsync(() =>
        {
            var groups = _repository.FindDuplicateTitles();
            return () =>
            {
                Duplicates.Clear();
                foreach (var group in groups)
                {
                    Duplicates.Add(new DuplicateRow(null, group.Reason, IsHeader: true));
                    foreach (var title in group.Titles)
                    {
                        var year = title.Year?.ToString(CultureInfo.CurrentCulture) ?? "?";
                        var files = title.Files == 0 ? Tr.T("aucun fichier") : Tr.F("{0} fichier(s)", title.Files);
                        Duplicates.Add(new DuplicateRow(title.Id, $"{title.Title} ({year}) · {files}", IsHeader: false));
                    }
                }

                Message = groups.Count == 0
                    ? Tr.T("Aucun film en double trouvé.")
                    : Tr.F("{0} groupe(s) de doublons possibles. Double-cliquez sur un titre pour l'afficher dans le catalogue.", groups.Count);
            };
        });
    }

    /// <summary>Ferme les réglages et affiche le titre choisi dans la liste des doublons.</summary>
    public void ShowDuplicate(DuplicateRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.TitleId is { } id)
        {
            PendingTitleId = id;
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RenumberAsync()
    {
        var label = CurrentOrder?.Label ?? Tr.T("Titre");
        var ids = CurrentOrder?.Ids() ?? [];
        return ModifyAsync(
            Tr.T("Renuméroter les films"),
            Tr.F("Renuméroter tous les exemplaires (colonne N°) de 1 à n selon le tri « {0} » ? Les numéros actuels sont remplacés.", label),
            "renumerotation",
            () =>
            {
                var count = _repository.Renumber(ids);
                ActivityJournal.Current.Write($"Maintenance : {count} exemplaire(s) renuméroté(s) selon le tri « {label} »");
                return Tr.F("{0} exemplaire(s) renuméroté(s).", count);
            });
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RemoveEmptyLabelsAsync() => ModifyAsync(
        Tr.T("Supprimer les collections, tags et groupes vides"),
        Tr.T("Supprimer les collections, tags et groupes qui ne contiennent plus aucun titre ?"),
        "classements-vides",
        () =>
        {
            var (collections, tags, groups) = _repository.RemoveEmptyLabels();
            ActivityJournal.Current.Write($"Maintenance : {collections} collection(s), {tags} tag(s), {groups} groupe(s) vides supprimés");
            return Tr.F("{0} collection(s), {1} tag(s) et {2} groupe(s) vides supprimés.", collections, tags, groups);
        });

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RemoveUnusedImagesAsync() => ModifyAsync(
        Tr.T("Nettoyer les images inutilisées"),
        Tr.T("Effacer les affiches, fonds et photos que plus aucun titre, collection ou personne n'utilise ?"),
        "images-inutilisees",
        () =>
        {
            var result = _repository.RemoveUnusedImages(_imagesDirectory);
            ActivityJournal.Current.Write($"Maintenance : {result.Rows} image(s) retirée(s), {result.Files} fichier(s) effacé(s)");
            return Tr.F("{0} image(s) retirée(s) de la base, {1} fichier(s) effacé(s) ({2}).", result.Rows, result.Files, FormatSize(result.Bytes));
        });

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task CheckIntegrityAsync() => RunAsync(() =>
    {
        var problems = _repository.CheckIntegrity();
        ActivityJournal.Current.Write($"Maintenance : contrôle d'intégrité, {problems.Count} anomalie(s)");
        return () => Message = problems.Count == 0
            ? Tr.T("Base en bon état : aucune anomalie.")
            : Tr.F("{0} anomalie(s) : {1}", problems.Count, string.Join(" ; ", problems.Take(10)));
    });

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task OptimizeAsync() => RunAsync(() =>
    {
        var (before, after) = _repository.Optimize();
        ActivityJournal.Current.Write($"Maintenance : base compactée ({before} → {after} octets)");
        return () => Message = Tr.F("Base compactée et optimisée : {0} → {1}.", FormatSize(before), FormatSize(after));
    });

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task BackupAsync() => RunAsync(() =>
    {
        var path = _repository.Backup(_backupsDirectory, "manuelle");
        ActivityJournal.Current.Write($"Maintenance : sauvegarde {path}");
        return () => Message = Tr.F("Sauvegarde créée : {0}", path);
    });

    /// <summary>
    /// Vide tout le catalogue ouvert après deux confirmations ; la base est sauvegardée juste avant (pas les images).
    /// Ses réglages, listes de référence, champs personnalisés et filtres enregistrés restent.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task PurgeCatalogAsync()
    {
        var info = _repository.Info();
        if (!await AskAsync(Tr.T("Purger le catalogue"),
                Tr.F("Effacer tout le contenu du catalogue « {0} » : {1} titre(s), {2} exemplaire(s), {3} personne(s) et toutes ses images ? Ses réglages sont gardés.",
                    CatalogName, info.Titles, info.Copies, info.People)) ||
            !await AskAsync(Tr.T("Dernière confirmation"),
                Tr.F("Le catalogue « {0} » va être vidé. Une sauvegarde de la base est faite juste avant, mais les images effacées ne pourront pas être récupérées. Purger maintenant ?", CatalogName)))
        {
            return;
        }

        await RunAsync(() =>
        {
            _repository.Backup(_backupsDirectory, "avant-purge");
            var purge = _repository.Purge(_imagesDirectory);
            Changed = true;
            ActivityJournal.Current.Write($"Maintenance : catalogue purgé ({purge.Titles} titre(s), {purge.People} personne(s), {purge.ImageFiles} image(s))");
            return () => Message = Tr.F("Catalogue purgé : {0} titre(s), {1} personne(s) et {2} image(s) ({3}) effacés.",
                purge.Titles, purge.People, purge.ImageFiles, FormatSize(purge.ImageBytes));
        });
    }

    /// <summary>Opération qui modifie la base : confirmation, sauvegarde préalable, exécution, compte rendu.</summary>
    private async Task ModifyAsync(string title, string question, string backupLabel, Func<string> action)
    {
        if (!await AskAsync(title, question + "\n\n" + Tr.T("Une sauvegarde de la base est faite juste avant.")))
        {
            return;
        }

        await RunAsync(() =>
        {
            _repository.Backup(_backupsDirectory, "avant-" + backupLabel);
            var result = action();
            Changed = true;
            return () => Message = result;
        });
    }

    private async Task<bool> AskAsync(string title, string message) => Confirm is null || await Confirm(title, message);

    /// <summary>Exécute le travail hors du fil de l'interface, puis applique son résultat sur le fil de l'interface.</summary>
    private async Task RunAsync(Func<Action> work)
    {
        IsBusy = true;
        Message = Tr.T("Opération en cours…");
        try
        {
            var apply = await Task.Run(work);
            apply();
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Message = ex.Message;
        }
        finally
        {
            IsBusy = false;
            RefreshInfo();
        }
    }

    private void RefreshInfo()
    {
        var info = _repository.Info();
        UnusedPeople = _repository.CountUnusedPeople();
        InfoText = Tr.F("{0} · {1} · version du schéma {2}\n{3} titre(s), {4} exemplaire(s), {5} personne(s), {6} image(s)",
            info.Path, FormatSize(info.SizeBytes), info.SchemaVersion, info.Titles, info.Copies, info.People, info.Images);
        OnPropertyChanged(nameof(RenumberLabel));
    }

    private static string FormatSize(long bytes) => bytes >= 1024 * 1024
        ? Tr.F("{0:0.0} Mo", bytes / 1024d / 1024d)
        : Tr.F("{0:0} Ko", bytes / 1024d);
}
