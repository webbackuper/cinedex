using Cinedex.App.Services;
using System.Collections.ObjectModel;
using Cinedex.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cinedex.Core.Localization;

namespace Cinedex.App.ViewModels;

/// <summary>Catalogues multiples : liste, ouverture, création, renommage, suppression (déplacement en sauvegarde).</summary>
public sealed partial class CatalogsViewModel : ObservableObject
{
    private readonly CatalogManager _manager;
    private readonly Action<string> _open;
    private readonly SettingsStore _currentSettings;

    public CatalogsViewModel(CatalogManager manager, string current, Action<string> open, SettingsStore currentSettings)
    {
        _manager = manager;
        _open = open;
        _currentSettings = currentSettings;
        Current = current;
        Reload();
    }

    public string Current { get; }

    public ObservableCollection<CatalogEntry> Catalogs { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand), nameof(RenameCommand), nameof(RemoveCommand))]
    public partial CatalogEntry? Selected { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand), nameof(RenameCommand))]
    public partial string NameText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? Message { get; set; }

    /// <summary>Vrai quand un autre catalogue a été ouvert : la fenêtre de gestion se ferme.</summary>
    [ObservableProperty]
    public partial bool Switched { get; set; }

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private void Open()
    {
        _open(Selected!.Name);
        Switched = true;
    }

    private bool CanOpen() => Selected is not null && Selected.Name != Current;

    /// <summary>Crée un catalogue vide qui reprend les réglages du catalogue ouvert (langue, MediaInfo, .nfo...), puis l'ouvre.</summary>
    [RelayCommand(CanExecute = nameof(HasName))]
    private void Create()
    {
        Run(() =>
        {
            var name = _manager.Create(NameText);
            ActivityJournal.Current.Write($"Catalogue créé : {name}");
            var settings = new SettingsStore(new CatalogDatabase(_manager.DatabasePath(name)));
            foreach (var (key, value) in _currentSettings.All())
            {
                settings.Set(key, value);
            }

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            _open(name);
            Switched = true;
        });
    }

    [RelayCommand(CanExecute = nameof(CanRename))]
    private void Rename()
    {
        Run(() =>
        {
            var name = _manager.Rename(Selected!.Name, NameText, Current);
            Message = Tr.F("Catalogue renommé en « {0} ».", name);
            ActivityJournal.Current.Write($"Catalogue renommé en « {name} »");
            NameText = string.Empty;
            Reload();
        });
    }

    private bool CanRename() => Selected is not null && Selected.Name != Current && HasName();

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private void Remove()
    {
        Run(() =>
        {
            var where = _manager.Remove(Selected!.Name, Current);
            Message = Tr.F("Catalogue retiré. Une copie complète est conservée dans {0}.", where);
            ActivityJournal.Current.Write($"Catalogue retiré (copie conservée dans {where})");
            Reload();
        });
    }

    private bool CanRemove() => Selected is not null && Selected.Name != Current;

    private bool HasName() => !string.IsNullOrWhiteSpace(NameText);

    private void Reload()
    {
        Catalogs.Clear();
        foreach (var catalog in _manager.List())
        {
            Catalogs.Add(catalog);
        }
    }

    private void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException or UnauthorizedAccessException)
        {
            Message = ex.Message;
        }
    }
}
