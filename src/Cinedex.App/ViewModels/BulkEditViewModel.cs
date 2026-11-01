using Cinedex.Core.Titles;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cinedex.App.ViewModels;

/// <summary>
/// Édition groupée : les cases à trois états restent indéterminées (aucun changement) tant qu'on n'y touche pas ;
/// les listes déroulantes proposent « ne pas modifier » par défaut.
/// </summary>
public sealed partial class BulkEditViewModel : ObservableObject
{
    public BulkEditViewModel(int count, IReadOnlyList<RefItem> media, IReadOnlyList<CollectionInfo> collections)
    {
        Header = $"Modifier {count} titres. Seuls les champs renseignés sont appliqués.";
        Media = [new(null, "(ne pas modifier)"), .. media.Select(m => new Choice<string?>(m.Code, m.Label))];
        Collections = [new(null, "(ne pas modifier)"), new(0, "(retirer de la collection)"), .. collections.Select(c => new Choice<long?>(c.Id, c.Name))];
        SelectedMedium = Media[0];
        SelectedCollection = Collections[0];
    }

    public string Header { get; }

    public IReadOnlyList<Choice<string?>> Media { get; }

    public IReadOnlyList<Choice<long?>> Collections { get; }

    [ObservableProperty] public partial bool? IsOwned { get; set; }

    [ObservableProperty] public partial bool? IsSeen { get; set; }

    [ObservableProperty] public partial bool? IsFavorite { get; set; }

    [ObservableProperty] public partial bool? IsShortlisted { get; set; }

    [ObservableProperty] public partial bool? IsLocked { get; set; }

    [ObservableProperty] public partial Choice<string?> SelectedMedium { get; set; }

    [ObservableProperty] public partial Choice<long?> SelectedCollection { get; set; }

    [ObservableProperty] public partial string Location { get; set; } = string.Empty;

    [ObservableProperty] public partial string AddTags { get; set; } = string.Empty;

    [ObservableProperty] public partial string RemoveTags { get; set; } = string.Empty;

    [ObservableProperty] public partial string AddGroups { get; set; } = string.Empty;

    public BulkEdit Build() => new()
    {
        IsOwned = IsOwned,
        IsSeen = IsSeen,
        IsFavorite = IsFavorite,
        IsShortlisted = IsShortlisted,
        IsLocked = IsLocked,
        MediumCode = SelectedMedium.Value,
        CollectionId = SelectedCollection.Value,
        Location = string.IsNullOrWhiteSpace(Location) ? null : Location.Trim(),
        AddTags = TitleEditorViewModel.SplitList(AddTags),
        RemoveTags = TitleEditorViewModel.SplitList(RemoveTags),
        AddGroups = TitleEditorViewModel.SplitList(AddGroups),
    };
}
