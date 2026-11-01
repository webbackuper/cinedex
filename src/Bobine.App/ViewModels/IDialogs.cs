using Bobine.Core.Metadata;

namespace Bobine.App.ViewModels;

/// <summary>Boîtes de dialogue ouvertes par les vues-modèles ; implémentée par la fenêtre principale.</summary>
public interface IDialogs
{
    Task<bool> EditTitleAsync(TitleEditorViewModel editor);

    Task<bool> ConfirmAsync(string title, string message);

    Task InformAsync(string title, string message);

    Task<MetadataSearchResult?> SearchMetadataAsync(MetadataSearchViewModel search);

    Task<bool> EditSettingsAsync(SettingsViewModel settings);

    Task<bool> RuleSearchAsync(RuleSearchViewModel search);

    Task<bool> BulkEditAsync(BulkEditViewModel edit);

    Task<bool> ImportFolderAsync(ImportFolderViewModel import);

    Task ManageCatalogsAsync(CatalogsViewModel catalogs);

    Task<bool> ShowSeriesAsync(SeriesViewModel series);

    Task<bool> ShowPersonAsync(PersonViewModel person);

    Task<bool> ShowPeopleAsync(PeopleBrowserViewModel people);
}
