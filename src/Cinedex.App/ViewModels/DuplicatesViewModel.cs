using Cinedex.App.Services;
using Cinedex.Core.Localization;
using Cinedex.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cinedex.App.ViewModels;

/// <summary>
/// Les films en double du bandeau gauche : les groupes à trancher, celui qu'on regarde, et les fusions.
/// <para>
/// Sorti de la fenêtre principale, qui n'a plus à en connaître que deux choses : quel groupe est choisi
/// (pour n'afficher que ses fiches) et qu'une fusion vient d'avoir lieu (pour relire sa liste).
/// </para>
/// </summary>
public sealed partial class DuplicatesViewModel : ObservableObject
{
    private readonly DuplicateMerger _merger;
    private readonly Func<string, string, Task<bool>> _confirm;

    /// <summary>La recherche a déjà été lancée : sans quoi « aucun doublon » et « pas encore cherché » se confondent.</summary>
    private bool _searched;

    public DuplicatesViewModel(DuplicateMerger merger, Func<string, string, Task<bool>> confirm)
    {
        ArgumentNullException.ThrowIfNull(merger);
        ArgumentNullException.ThrowIfNull(confirm);
        _merger = merger;
        _confirm = confirm;
    }

    /// <summary>Une fusion vient d'être faite : le catalogue a changé, celui qui l'affiche doit le relire.</summary>
    public event EventHandler? Merged;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGroups), nameof(StatusText))]
    [NotifyCanExecuteChangedFor(nameof(MergeAllCommand))]
    public partial IReadOnlyList<DuplicateGroupItem> Groups { get; private set; } = [];

    public bool HasGroups => Groups.Count > 0;

    public string StatusText => !_searched
        ? Tr.T("Cherchez les films présents plusieurs fois au catalogue.")
        : Groups.Count == 0
            ? Tr.T("Aucun film en double au catalogue.")
            : Tr.F("{0} groupe(s) à trancher. Choisissez un groupe pour n'afficher que ses fiches.", Groups.Count);

    /// <summary>Dernière chose à dire à l'utilisateur : le résultat d'une recherche ou d'une fusion.</summary>
    [ObservableProperty]
    public partial string? Message { get; private set; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    /// <summary>Groupe choisi : la liste principale ne montre que ses fiches, avec toutes les actions usuelles.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelected))]
    [NotifyCanExecuteChangedFor(nameof(MergeSelectedCommand))]
    public partial DuplicateGroupItem? Selected { get; set; }

    public bool HasSelected => Selected is not null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FindCommand), nameof(MergeAllCommand), nameof(MergeSelectedCommand))]
    public partial bool IsBusy { get; private set; }

    /// <summary>Ouvrir le menu cherche les doublons la première fois : on vient le consulter, pas l'armer.</summary>
    [RelayCommand]
    private async Task ToggleAsync()
    {
        IsExpanded = !IsExpanded;
        if (IsExpanded && !_searched)
        {
            await FindAsync();
        }
    }

    private bool CanFind() => !IsBusy;

    /// <summary>Cherche les films en double. Rien n'est modifié : c'est une lecture du catalogue.</summary>
    [RelayCommand(CanExecute = nameof(CanFind))]
    private async Task FindAsync()
    {
        IsBusy = true;
        try
        {
            await SearchAsync();
            Message = StatusText;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanMergeAll() => !IsBusy && Groups.Count > 0;

    /// <summary>
    /// Fusionne tous les groupes d'un coup. Chaque fiche gardée réunit les fichiers de celles qu'elle remplace :
    /// un même film en plusieurs versions, un fichier par version. Aucun fichier du disque n'est touché.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanMergeAll))]
    private Task MergeAllAsync() => MergeAsync(
        Tr.F("Regrouper les {0} groupe(s) de doublons, chacun en une seule fiche qui gardera tous ses fichiers vidéo ?\n\nAucun fichier du disque n'est touché, et la base est sauvegardée avant.", Groups.Count),
        _merger.MergeAll);

    private bool CanMergeSelected() => !IsBusy && Selected is not null;

    /// <summary>
    /// Fusionne le seul groupe affiché. Tous les rapprochements proposés ne se valent pas : pouvoir trancher
    /// groupe par groupe évite d'avoir à tout accepter ou tout refuser.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanMergeSelected))]
    private Task MergeSelectedAsync() => Selected is not { } group
        ? Task.CompletedTask
        : MergeAsync(
            Tr.F("Regrouper en une seule fiche le groupe « {0} » ?\n\nLes autres groupes ne sont pas touchés, et aucun fichier du disque non plus.", group.Label),
            () => _merger.MergeChosen(group.TitleIds));

    private async Task MergeAsync(string question, Func<TitleMerge> merge)
    {
        if (!await _confirm("Fusionner les films en double", question))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await Task.Run(merge);
            Selected = null;

            // La liste porte encore les groupes disparus : elle est relue pour ne montrer que ce qui reste à trancher.
            await SearchAsync();
            Message = DuplicateMerger.Describe(result);
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException or UnauthorizedAccessException)
        {
            // La sauvegarde ou la fusion a échoué : on le dit, et rien n'est laissé à moitié (la fusion est une transaction).
            Message = ex.Message;
            return;
        }
        finally
        {
            IsBusy = false;
        }

        Merged?.Invoke(this, EventArgs.Empty);
    }

    private async Task SearchAsync()
    {
        var groups = await Task.Run(_merger.Find);
        var chosen = Selected?.Label;
        Groups = [.. groups.Select(DuplicateGroupItem.From)];
        _searched = true;
        OnPropertyChanged(nameof(StatusText));
        Selected = chosen is null ? null : Groups.FirstOrDefault(g => string.Equals(g.Label, chosen, StringComparison.Ordinal));
    }
}
