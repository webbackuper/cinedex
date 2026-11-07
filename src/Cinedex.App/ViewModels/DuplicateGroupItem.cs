using Cinedex.Core.Localization;
using Cinedex.Data;

namespace Cinedex.App.ViewModels;

/// <summary>
/// Un groupe de films en double, tel qu'il apparaît dans le bandeau gauche. Le libellé nomme le groupe par
/// son premier titre, puisque c'est ainsi qu'on le reconnaît ; le motif dit pourquoi ces fiches ont été
/// rapprochées, et les identifiants servent à n'afficher que ce groupe dans la liste principale.
/// </summary>
public sealed record DuplicateGroupItem(string Label, string Reason, IReadOnlyList<long> TitleIds)
{
    public static DuplicateGroupItem From(DuplicateGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        var first = group.Titles.Count > 0 ? group.Titles[0] : null;
        var name = first is null
            ? Tr.T("Groupe sans titre")
            : first.Year is { } year
                ? $"{first.Title} ({year})"
                : first.Title;

        return new DuplicateGroupItem(
            Tr.F("{0} · {1} fiches", name, group.Titles.Count),
            group.Reason,
            [.. group.Titles.Select(t => t.Id)]);
    }
}
