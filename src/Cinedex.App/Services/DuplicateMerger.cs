using Cinedex.Core.Localization;
using Cinedex.Data;

namespace Cinedex.App.Services;

/// <summary>
/// Recherche et fusion des films en double, pour tous ceux qui les proposent : le bandeau gauche, l'onglet
/// Maintenance des réglages, le clic droit sur une sélection.
/// <para>
/// Il n'y a qu'un endroit où une fusion se fait, et il commence par une sauvegarde de la base. Tant que
/// chaque écran fusionnait de son côté, deux sur trois annonçaient cette sauvegarde sans la faire : une
/// fusion ne se défait pas, la promesse doit donc être tenue par le code, pas par le texte de la question.
/// </para>
/// </summary>
public sealed class DuplicateMerger(MaintenanceRepository repository, string backupsDirectory)
{
    /// <summary>Les groupes de films en double. Une lecture : rien n'est modifié.</summary>
    public IReadOnlyList<DuplicateGroup> Find() => repository.FindDuplicateTitles();

    /// <summary>Fusionne tous les groupes, chacun en une fiche qui garde tous ses fichiers.</summary>
    public TitleMerge MergeAll() => Merge("fusion-doublons", repository.MergeDuplicateTitles, "tous les groupes de doublons");

    /// <summary>Fusionne le seul groupe auquel ce titre appartient.</summary>
    public TitleMerge MergeGroupOf(long titleId) =>
        Merge("fusion-doublon", () => repository.MergeDuplicateTitles(titleId), "un groupe de doublons");

    /// <summary>
    /// Fusionne des fiches désignées par l'utilisateur. Rien ne vérifie qu'il s'agit du même film : c'est lui
    /// qui l'affirme, et c'est une raison de plus pour sauvegarder avant.
    /// </summary>
    public TitleMerge MergeChosen(IReadOnlyCollection<long> titleIds)
    {
        ArgumentNullException.ThrowIfNull(titleIds);
        return Merge("fusion-selection", () => repository.MergeChosenTitles(titleIds), $"{titleIds.Count} fiche(s) désignée(s)");
    }

    /// <summary>Ce que la fusion a fait, en une phrase à montrer.</summary>
    public static string Describe(TitleMerge result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Merged == 0
            ? Tr.T("Aucune fiche en double à fusionner.")
            : Tr.F("{0} fiche(s) fusionnée(s), {1} fichier(s) regroupé(s), dont {2} de caractéristiques identiques à un autre du même film.",
                result.Merged, result.Files, result.SameEncoding);
    }

    private TitleMerge Merge(string label, Func<TitleMerge> merge, string what)
    {
        // La sauvegarde d'abord, et dans tous les cas : si elle échoue, la fusion n'a pas lieu.
        repository.Backup(backupsDirectory, "avant-" + label);
        var result = merge();
        ActivityJournal.Current.Write(
            $"Fusion de {what} : {result.Merged} fiche(s) retirée(s), {result.Files} fichier(s) regroupé(s), {result.SameEncoding} de même encodage");
        return result;
    }
}
