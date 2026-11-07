using Cinedex.Core.Metadata;

namespace Cinedex.App.ViewModels;

/// <summary>Boîtes de dialogue ouvertes par les vues-modèles ; implémentée par la fenêtre principale.</summary>
public interface IDialogs
{
    Task<bool> EditTitleAsync(TitleEditorViewModel editor);

    Task<bool> ConfirmAsync(string title, string message);

    Task InformAsync(string title, string message);

    /// <summary>Met un texte dans le presse-papiers ; renvoie faux si le système ne le permet pas.</summary>
    Task<bool> CopyTextAsync(string text);

    Task<MetadataSearchResult?> SearchMetadataAsync(MetadataSearchViewModel search);

    Task<bool> EditSettingsAsync(SettingsViewModel settings);

    Task<bool> RuleSearchAsync(RuleSearchViewModel search);

    Task<bool> BulkEditAsync(BulkEditViewModel edit);

    Task<bool> ImportFolderAsync(ImportFolderViewModel import);

    Task ManageCatalogsAsync(CatalogsViewModel catalogs);

    Task<bool> ShowSeriesAsync(SeriesViewModel series);

    Task<bool> ShowPersonAsync(PersonViewModel person);

    Task<bool> ShowMatchReviewAsync(MatchReviewViewModel review);

    Task<bool> ShowCollectionsAsync(CollectionsViewModel collections);

    Task<bool> ShowSeriesTrackingAsync(SeriesTrackingViewModel tracking);

    /// <summary>Ouvre l'aide (fenêtre indépendante, non bloquante) ou la ramène au premier plan.</summary>
    void ShowHelp(HelpViewModel help);
}
