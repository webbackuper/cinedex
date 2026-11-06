using Cinedex.Core.Localization;
using Cinedex.Core.Titles;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cinedex.App.ViewModels;

/// <summary>Une entrée d'un sous-menu de parcours : son libellé, et la recherche qu'un clic applique.</summary>
public sealed record FacetItem(string Label, RuleSearch Search)
{
    public override string ToString() => Label;
}

/// <summary>
/// Un sous-menu dépliable du bandeau gauche (genres, décennies, pays, langues). Chacun se comporte comme le
/// sous-menu MediaID : un clic remplace la recherche en cours, et choisir ailleurs relâche la sélection.
/// </summary>
public sealed partial class FacetGroup(string label, string icon, IReadOnlyList<FacetItem> items, Action<FacetGroup> onSelected)
    : ObservableObject
{
    public string Label { get; } = label;

    /// <summary>
    /// Nom lu par les lecteurs d'écran. Distinct du libellé : « Genres » nomme déjà les pastilles de la fiche,
    /// et deux contrôles de même nom se confondent à la lecture.
    /// </summary>
    public string ListName { get; } = Tr.F("Parcourir par {0}", label);

    public string Icon { get; } = icon;

    public IReadOnlyList<FacetItem> Items { get; } = items;

    public bool HasItems => Items.Count > 0;

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    [ObservableProperty]
    public partial FacetItem? SelectedItem { get; set; }

    [RelayCommand]
    private void Toggle() => IsExpanded = !IsExpanded;

    partial void OnSelectedItemChanged(FacetItem? value)
    {
        if (value is not null)
        {
            onSelected(this);
        }
    }

    /// <summary>Relâche la sélection sans rien déclencher : une autre vue vient d'être choisie.</summary>
    public void Clear() => SelectedItem = null;
}
