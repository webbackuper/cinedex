using Cinedex.App.Services;
using Cinedex.Core.Localization;
using Cinedex.Core.Titles;
using Cinedex.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cinedex.App.ViewModels;

/// <summary>
/// Les films en double, gérés dans la zone centrale de la fenêtre principale : tous les groupes à la fois,
/// chacun avec ses fiches, la raison du rapprochement et de quoi le fusionner. Le bandeau gauche n'en porte
/// plus qu'une entrée « Doublons (n) », sans liste dépliable, et les réglages y renvoient.
/// <para>
/// La fenêtre principale ne fait qu'héberger ce modèle : elle sait quand la vue s'ouvre (pour fermer les
/// autres) et qu'une fusion a eu lieu (pour relire sa liste).
/// </para>
/// </summary>
public sealed partial class DuplicatesViewModel : ObservableObject
{
    private readonly DuplicateMerger _merger;
    private readonly Func<string, string, Task<bool>> _confirm;
    private readonly Func<IReadOnlyList<long>, IReadOnlyList<TitleSummary>>? _prepare;

    /// <summary>La recherche a déjà été lancée : sans quoi « aucun doublon » et « pas encore cherché » se confondent.</summary>
    private bool _searched;

    /// <param name="prepare">Lignes affichables des fiches d'un groupe, dans l'ordre donné (affiche, sous-titre).</param>
    public DuplicatesViewModel(DuplicateMerger merger, Func<string, string, Task<bool>> confirm, Func<IReadOnlyList<long>, IReadOnlyList<TitleSummary>>? prepare = null)
    {
        ArgumentNullException.ThrowIfNull(merger);
        ArgumentNullException.ThrowIfNull(confirm);
        _merger = merger;
        _confirm = confirm;
        _prepare = prepare;
    }

    /// <summary>Une fusion vient d'être faite : le catalogue a changé, celui qui l'affiche doit le relire.</summary>
    public event EventHandler? Merged;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGroups), nameof(StatusText), nameof(EntryLabel))]
    [NotifyCanExecuteChangedFor(nameof(MergeAllCommand))]
    public partial IReadOnlyList<DuplicateGroupItem> Groups { get; private set; } = [];

    public bool HasGroups => Groups.Count > 0;

    public string StatusText => !_searched
        ? Tr.T("Cherchez les films présents plusieurs fois au catalogue.")
        : Groups.Count == 0
            ? Tr.T("Aucun film en double au catalogue.")
            : Tr.F("{0} groupe(s) à trancher. Vérifiez qu'il s'agit bien du même film avant de fusionner : le rapprochement est une proposition.", Groups.Count);

    /// <summary>L'entrée du bandeau gauche : « Doublons (3) » une fois la recherche faite.</summary>
    public string EntryLabel => _searched && Groups.Count > 0 ? Tr.F("Doublons ({0})", Groups.Count) : Tr.T("Doublons");

    /// <summary>Dernière chose à dire à l'utilisateur : le résultat d'une recherche ou d'une fusion.</summary>
    [ObservableProperty]
    public partial string? Message { get; private set; }

    /// <summary>La vue des doublons occupe la zone centrale.</summary>
    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FindCommand), nameof(MergeAllCommand), nameof(MergeGroupCommand))]
    public partial bool IsBusy { get; private set; }

    /// <summary>Ouvre la vue, doublons cherchés à nouveau : un import ou une fusion depuis le clic droit a pu tout changer.</summary>
    [RelayCommand]
    private async Task OpenAsync()
    {
        IsOpen = true;
        await FindAsync();
    }

    [RelayCommand]
    private void Close() => IsOpen = false;

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

    private bool CanMergeGroup(DuplicateGroupItem? group) => !IsBusy && group is not null;

    /// <summary>
    /// Fusionne un seul groupe. Tous les rapprochements proposés ne se valent pas : pouvoir trancher groupe
    /// par groupe évite d'avoir à tout accepter ou tout refuser.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanMergeGroup))]
    private Task MergeGroupAsync(DuplicateGroupItem? group) => group is null
        ? Task.CompletedTask
        : MergeAsync(
            Tr.F("Regrouper en une seule fiche le groupe « {0} » ?\n\nLes autres groupes ne sont pas touchés, et aucun fichier du disque non plus.", group.Label),
            () => _merger.MergeChosen(group.TitleIds));

    /// <summary>Le groupe d'un titre affiché dans la vue (pour ouvrir sa fiche en parcourant son groupe).</summary>
    public DuplicateGroupItem? GroupOf(TitleSummary title) =>
        Groups.FirstOrDefault(g => g.Titles.Any(t => ReferenceEquals(t, title))) ?? Groups.FirstOrDefault(g => g.TitleIds.Contains(title.Id));

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

            // La vue porte encore les groupes disparus : elle est relue pour ne montrer que ce qui reste à trancher.
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
        _searched = true;
        Groups = [.. groups.Select(g =>
        {
            var item = DuplicateGroupItem.From(g);
            return _prepare is null ? item : item with { Titles = _prepare(item.TitleIds) };
        })];
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(EntryLabel));
    }
}
